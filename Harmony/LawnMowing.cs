using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

public class OcbLawnMowing : IModApi
{
    public void InitMod(Mod mod)
    {
        Log.Out("Loading OCB Lawn Mowing Patch: " + GetType().ToString());
        var harmony = new Harmony(GetType().ToString());
        harmony.PatchAll(Assembly.GetExecutingAssembly());
    }

    [HarmonyPatch(typeof(EntityVehicle))]
    [HarmonyPatch("HasHeadlight")]
    public class EntityVehicle_HasHeadlight
    {
        public static bool Prefix(
            EntityVehicle __instance,
            ref bool __result)
        {
            __result = __instance.GetVehicle().FindPart("mower") is VPMower;
            return !__result; // Check base if not yet true
        }
    }

    [HarmonyPatch(typeof(EntityVehicle))]
    [HarmonyPatch("IsHeadlightOn", MethodType.Getter)]
    public class EntityVehicle_IsHeadlightOn_get
    {
        public static bool Prefix(
            EntityVehicle __instance,
            ref bool __result)
        {
            __result = __instance.GetVehicle().FindPart("mower") is VPMower part && part.IsOn;
            return !__result; // Check base if not yet true
        }
    }

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
