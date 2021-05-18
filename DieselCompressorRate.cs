using HarmonyLib;
using UnityEngine;

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
    }
}