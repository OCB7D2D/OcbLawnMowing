using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public class OcbLawnMowing : IModApi
{

    // ####################################################################
    // ####################################################################

    public void InitMod(Mod mod)
    {
        Log.Out("OCB Harmony Patch: " + GetType().ToString());
        Harmony harmony = new Harmony(GetType().ToString());
        harmony.PatchAll(Assembly.GetExecutingAssembly());
    }

    // ####################################################################
    // We want to display harvested items on the side bar, but we want to
    // do so by using the actual bag instead of amount in player inventory
    // ####################################################################

    // Set this static value to override behavior
    public static Bag UseBagForItemCount = null;

    [HarmonyPatch(typeof(XUiM_PlayerInventory))]
    [HarmonyPatch("GetItemCountWithMods")]
    public class PlayerInventoryGetItemCountPatch
    {
        public static bool Prefix(ItemValue _itemValue, ref int __result)
        {
            if (UseBagForItemCount == null) return true;
            __result = UseBagForItemCount.GetItemCount(_itemValue);
            return false;
        }
    }

    // ####################################################################
    // Currently you can either dye your vehicle with dyes or it is white
    // This allows a default tint color if no dye cosmetics are installed
    // ####################################################################

    [HarmonyPatch(typeof(ItemValue))]
    [HarmonyPatch("GetPropertyOverride")]
    public class ItemValueGetPropertyOverridePatch
    {
        public static void Prefix(
            ItemValue __instance,
            string _propertyName,
            ref string _originalValue)
        {
            // Only apply this change to TintColor properties
            // Otherwise not sure if it interferes with stuff
            if (_propertyName != Block.PropTintColor) return;
            // Try to update the original value from properties
            // Basically use XML property of item if it exists
            __instance.ItemClass.Properties.ParseString(
                Block.PropTintColor, ref _originalValue);
        }
    }

    // ####################################################################
    // ####################################################################

    // Hook to inform mower of new modifiers
    [HarmonyPatch(typeof(Vehicle))]
    [HarmonyPatch("SetItemValueMods")]
    public class VehicleSetItemValueModsPatch
    {
        public static void Postfix(
            Vehicle __instance,
            ItemValue ___itemValue)
        {
            if (__instance.FindPart("mower") is VPMower mower)
                mower.UpdateModifications(___itemValue.Modifications);
        }
    }

    // Hook to inform mower of new modifiers
    [HarmonyPatch(typeof(Vehicle))]
    [HarmonyPatch("SetItemValue")]
    public class VehicleSetItemValuePatch
    {
        public static void Postfix(
            Vehicle __instance,
            ItemValue ___itemValue)
        {
            if (__instance.FindPart("mower") is VPMower mower)
                mower.UpdateModifications(___itemValue.Modifications);
        }
    }

    // ####################################################################
    // Fix vanilla block extending for harvest
    // We can have the same item to harvest with different tags
    // Vanilla incorrectly detects the same name and cuts them
    // ####################################################################

    [HarmonyPatch(typeof(Block))]
    [HarmonyPatch("CopyDroppedFrom")]
    public class BlockCopyDroppedFromPatch
    {
        public static bool Prefix(
            Block __instance,
            Block _other)
        {
            // Only patch extending of a specific block we checked
            // I think this is the right approach, but keep vanilla as is
            bool harvests = _other.Properties.GetBool("ExtendHarvestDrops");
            bool destroys = _other.Properties.GetBool("ExtendDestroyDrops");
            // Abort patcher if nothing is enabled for block
            if (harvests == false && destroys == false) return true;
            // Code below is mostly just copied from `Block.CopyDroppedFrom`
            foreach (KeyValuePair<EnumDropEvent, List<Block.SItemDropProb>> okv in _other.itemsToDrop)
            {
                EnumDropEvent evt = okv.Key;
                List<Block.SItemDropProb> others = okv.Value;
                // Create a new list for key if it doesn't exist
                if (!__instance.itemsToDrop.TryGetValue(evt,
                    out List<Block.SItemDropProb> ours))
                {
                    __instance.itemsToDrop[evt] = ours =
                        new List<Block.SItemDropProb>();
                }
                // Use our patched code to extend harvest or destroy
                if ((harvests && evt == EnumDropEvent.Harvest) || (destroys && evt == EnumDropEvent.Destroy))
                {
                    int count = ours.Count;
                    for (int i = 0; i < others.Count; ++i)
                    {
                        bool flag = true;
                        for (int n = 0; flag && n < count; ++n)
                        {
                            if (ours[n].name == others[i].name)
                            {
                                flag = false;
                                break;
                            }
                        }
                        if (flag) ours.Add(others[i]);
                    }
                }
                // Use default code to overwrite on drop/fall
                else
                {
                    for (int i = 0; i < others.Count; ++i)
                    {
                        bool flag = true;
                        for (int n = 0; flag && n < ours.Count; ++n)
                        {
                            if (ours[n].name == others[i].name)
                            {
                                flag = false;
                                break;
                            }
                        }
                        if (flag) ours.Add(others[i]);
                    }
                }
            }
            return false;
        }
    }

    // ####################################################################
    // Little easter egg to make your lawn tractor more durable :)
    // ####################################################################

    [HarmonyPatch(typeof(EntityVehicle))]
    [HarmonyPatch("ApplyDamage")]
    public class EntityVehicleApplyDamagePatch
    {
        private static void Prefix(EntityVehicle __instance, ref int damage)
        {
            foreach (var part in __instance.vehicle.GetParts())
            {
                if (!(part is VPMower mower)) continue;
                damage = (int)(mower.DamageModifier * damage);
            }
        }
    }

    [HarmonyPatch(typeof(Audio.Manager), "Play")]
    [HarmonyPatch(new System.Type[] { typeof(Vector3),
        typeof(string), typeof(int) })]
    public class AudioManagerPlayPatch
    {
        private static void Prefix(string soundGroupName, int entityId, ref bool __state)
        {
            if (entityId >= 0 && soundGroupName == "lawnmower_plant")
            {
                var entity = GameManager.Instance.World.GetEntity(entityId);
                if (entity is EntityAlive alive) __state = alive.Crouching;
                ((EntityAlive)entity).Crouching = true;
            }
        }
        private static void Postfix(string soundGroupName, int entityId, bool __state)
        {
            if (entityId >= 0 && soundGroupName == "lawnmower_plant")
            {
                var entity = GameManager.Instance.World.GetEntity(entityId);
                if (entity is EntityAlive alive) alive.Crouching = __state;
            }
        }
    }


    // ####################################################################
    // Make sure paint is set on all renderers (also on LODs)
    // ####################################################################

    [HarmonyPatch(typeof(VehiclePart))]
    [HarmonyPatch("SetColors")]
    public class VehiclePartSetColorsPatch
    {
        private static void Postfix(VehiclePart __instance, Color _color)
        {
            if (!(__instance.GetTransform("paints") is Transform transform)) return;
            System.Array.ForEach(transform.GetComponentsInChildren<Renderer>(),
                renderer => renderer.material.color = _color);
        }
    }

    // ####################################################################
    // Allow vehicles to re-use physics colliders for interaction
    // ####################################################################

    [HarmonyPatch(typeof(GameUtils))]
    [HarmonyPatch("GetHitRootTransform")]
    public class GameUtilsGetHitRootTransformPatch
    {
        private static bool Prefix(string _tag, Transform _hitTransform, ref Transform __result)
        {
            if (_tag != "E_Vehicle") return true;
            RootTransformRefEntity component = _hitTransform.GetComponent<RootTransformRefEntity>();
            __result = component?.RootTransform ?? RootTransformRefEntity.FindEntityUpwards(_hitTransform);
            return false;
        }
    }

    // ####################################################################
    // Patch sidebar item counter to just count up and down
    // Otherwise re-seeding can get pretty weird
    // ####################################################################

    [HarmonyPatch(typeof(XUiC_CollectedItemList), "AddItemStack")]
    static class CollectedItemListAddItemStackPatch
    {
        static int CheckToSimplyCountAddedItems(int a, int b)
         => UseBagForItemCount == null ? a : b;
        // Main function handling the IL patching
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var position = 105; // ToDo: Make more dynamic!
            var codes = new List<CodeInstruction>(instructions);
            codes.InsertRange(position, new CodeInstruction[]
            {
                CodeInstruction.Call(
                    typeof(CollectedItemListAddItemStackPatch),
                    "CheckToSimplyCountAddedItems"),
                codes[position - 1]
            });
            return codes;
        }
    }

    // ####################################################################
    // ####################################################################

    // Patch to enhance reporting of mod unlocking
    // Shows the unlock level in the UI for these mod
    [HarmonyPatch(typeof(RecipeUnlockData))]
    [HarmonyPatch("GetLevel")]
    public class RecipeUnlockDataGetLevelPatch
    {
        public static bool Prefix(
            RecipeUnlockData __instance,
            string recipeName,
            ref string __result)
        {
            if (__instance.unlockType == RecipeUnlockData.UnlockTypes.Perk)
            {
                string pname = "UnlockFor"
                    + Char.ToUpper(__instance.perk.Name[0])
                    + __instance.perk.Name.Substring(1);
                if (ItemClass.GetItemClass(recipeName) is ItemClass item)
                {
                    var level = item.Properties.GetString(pname);
                    if (string.IsNullOrEmpty(level)) return true;
                    __result = level;
                    return false;
                }
            }
            return true;
        }
    }

    // ####################################################################
    // ####################################################################

}
