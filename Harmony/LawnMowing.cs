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

}
