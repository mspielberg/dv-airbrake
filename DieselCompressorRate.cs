using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace DvMod.AirBrake
{
    public static class DieselCompressorRate
    {
        private const float BaseRate = 0.8f;

        [HarmonyPatch(typeof(DieselLocoSimulation), nameof(DieselLocoSimulation.SimulateEngineRPM))]
        public static class DieselSimulateEngineRPMPatch
        {
            public static void Postfix(DieselLocoSimulation __instance)
            {
                var car = TrainCar.Resolve(__instance.gameObject);
                car.brakeSystem.compressorProductionRate = (1 + (__instance.engineRPM.value * 825f / 275f)) * BaseRate;
            }
        }

        [HarmonyPatch(typeof(ShunterLocoSimulation), nameof(ShunterLocoSimulation.SimulateEngineRPM))]
        public static class ShunterSimulateEngineRPMPatch
        {
            public static void Postfix(ShunterLocoSimulation __instance)
            {
                var car = TrainCar.Resolve(__instance.gameObject);
                car.brakeSystem.compressorProductionRate = (1 + (__instance.engineRPM.value * 2100f / 1250f)) * BaseRate;
            }
        }

        [HarmonyPatch]
        public static class CCLCompressorPatch
        {
            public static bool Prepare()
            {
                return UnityModManager.FindMod("DVCustomCarLoader")?.Active ?? false;
            }

            public static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    "DVCustomCarLoader.LocoComponents.DieselElectric.CustomLocoSimDiesel:SimulateEngineRPM",
                    new Type[1] { typeof(float) });
            }

            private static readonly FieldInfo rpmField = AccessTools.Field(
                AccessTools.TypeByName("DVCustomCarLoader.LocoComponents.DieselElectric.CustomLocoSimDiesel"),
                "engineRPM");

            public static void Prefix(Component __instance)
            {
                SimComponent field = (SimComponent)rpmField.GetValue(__instance);
                TrainCar car = TrainCar.Resolve(__instance.gameObject);
                car.brakeSystem.compressorProductionRate = (1 + (field.value * 2f)) * BaseRate;
            }
        }
    }
}
