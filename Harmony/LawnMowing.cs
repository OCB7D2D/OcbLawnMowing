using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

public class OcbLawnMowing : IModApi
{
    public void InitMod(Mod mod)
    {
        Log.Out("Loading OCB Lawn Mowing Patch: " + GetType().ToString());
        var harmony = new Harmony(GetType().ToString());
        harmony.PatchAll(Assembly.GetExecutingAssembly());
    }

    // Currently you can either dye your vehicle with dyes or it is white
    // This allows a default tint color if no dye cosmetics are installed
    [HarmonyPatch(typeof(ItemValue))]
    [HarmonyPatch("GetPropertyOverride")]
    public class ItemValue_GetPropertyOverride
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

    // Hook to inform mower of new modifiers
    [HarmonyPatch(typeof(Vehicle))]
    [HarmonyPatch("SetItemValueMods")]
    public class Vehicle_SetItemValueMods
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
    public class Vehicle_SetItemValue
    {
        public static void Postfix(
            Vehicle __instance,
            ItemValue ___itemValue)
        {
            if (__instance.FindPart("mower") is VPMower mower)
                mower.UpdateModifications(___itemValue.Modifications);
        }
    }

    // Fix vanilla block extending for harvest
    // We can have the same item to harvest with different tags
    // Vanilla incorrectly detects the same name and cuts them
    [HarmonyPatch(typeof(Block))]
    [HarmonyPatch("CopyDroppedFrom")]
    public class CopyDroppedFrom_CopyDroppedFrom
    {
        public static bool Prefix(
            Block __instance,
            Block _other)
        {
            bool enabled = false;
            // Only patch extending of a specific block we checked
            // I think this is the right approach, but keep vanilla as is
            _other.Properties.ParseBool("ExtendHarvestDrops", ref enabled);
            // Abort patcher if not enabled for block
            if (enabled == false) return true;
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
                // Use our patched code to extend harvest
                if (evt == EnumDropEvent.Harvest)
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

}
