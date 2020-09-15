using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DvMod.AirBrake
{
    public static class Indicators
    {
        private const string EqualizingReservoirPressure = "AirBrake.EqualizingReservoirPressure";
        private const string BrakeCylinderPressure = "AirBrake.BrakeCylinderPressure";

        private static string GetPath(Component c)
        {
            return string.Join("/", c.GetComponentsInParent<Transform>(true).Reverse().Select(c => c.name));
        }

        private static string DumpHierarchy(GameObject gameObject)
        {
            return string.Join("\n", gameObject.GetComponentsInChildren<Component>().Select(c => $"{GetPath(c)} {c.GetType()}"));
        }

        private static Material? redMaterial;
        private static void ChangeToRedNeedle(Indicator gauge)
        {
            Main.DebugLog($"gauge = {gauge}");
            var needle = gauge.transform.GetChild(0);
            Main.DebugLog($"needle = {needle}");
            Main.DebugLog(DumpHierarchy(gauge.gameObject));
            var renderer = needle.GetComponentInChildren<MeshRenderer>();
            if (redMaterial == null)
            {
                redMaterial = new Material(renderer.sharedMaterial)
                {
                    mainTexture = null,
                    color = Color.HSVToRGB(0.01f, 0.8f, 0.7f),
                };
            }
            renderer.sharedMaterial = redMaterial;
            needle.transform.localScale = new Vector3(0.95f, 1f, 0.95f);
            needle.transform.localPosition = new Vector3(0f, -0.001f, 0f);
        }

        private static void InitializeGauges(GameObject interior)
        {
            if (interior == null)
                return;
            Main.DebugLog("InitializeGauges");
            var (mainResIndicator, brakePipeIndicator) = TrainCar.Resolve(interior).carType switch
            {
                TrainCarType.LocoDiesel =>
                (
                    interior.GetComponent<IndicatorsDiesel>().brakeAux,
                    interior.GetComponent<IndicatorsDiesel>().brakePipe
                ),
                TrainCarType.LocoShunter =>
                (
                    interior.GetComponent<IndicatorsShunter>().brakeAux,
                    interior.GetComponent<IndicatorsShunter>().brakePipe
                ),
                TrainCarType.LocoSteamHeavy =>
                (
                    interior.GetComponent<IndicatorsSteam>().brakeAux,
                    interior.GetComponent<IndicatorsSteam>().brakePipe
                ),
                _ => default
            };
            if (mainResIndicator != null)
            {
                mainResIndicator.maxValue = Constants.MaxMainReservoirPressure;
                var equalizationReservoirIndicator = Object.Instantiate(mainResIndicator, mainResIndicator.transform.parent);
                equalizationReservoirIndicator.name = EqualizingReservoirPressure;
                ChangeToRedNeedle(mainResIndicator);
            }
            if (brakePipeIndicator != null)
            {
                brakePipeIndicator.maxValue = Constants.MaxBrakePipePressure;
                var brakeCylinderIndicator = Object.Instantiate(brakePipeIndicator, brakePipeIndicator.transform.parent);
                brakeCylinderIndicator.name = BrakeCylinderPressure;
                ChangeToRedNeedle(brakeCylinderIndicator);
            }
        }

        [HarmonyPatch(typeof(TrainCar), nameof(TrainCar.Awake))]
        public static class TrainCarAwakePatch
        {
            public static void Postfix(TrainCar __instance)
            {
                __instance.brakeSystem.brakePipePressure = 0f;
                __instance.brakeSystem.mainReservoirPressure = 0f;
                __instance.brakeSystem.mainReservoirPressureUnsmoothed = 0f;

                __instance.InteriorPrefabLoaded += InitializeGauges;
            }
        }

        private readonly struct ExtraIndicators
        {
            public readonly Indicator brakeCylinder;
            public readonly Indicator equalizingReservoir;

            public ExtraIndicators(Indicator brakeCylinder, Indicator equalizingReservoir)
            {
                this.brakeCylinder = brakeCylinder;
                this.equalizingReservoir = equalizingReservoir;
            }
        }

        [HarmonyPatch(typeof(IndicatorsShunter), nameof(IndicatorsShunter.Update))]
        public static class IndicatorsShunterPatch
        {
            private readonly static Cache<IndicatorsShunter, ExtraIndicators> extraIndicators =
                new Cache<IndicatorsShunter, ExtraIndicators>(
                    indicators =>
                    {
                        // Main.DebugLog(GetPath(indicators));
                        // Main.DebugLog(DumpHierarchy(indicators.gameObject));
                        return new ExtraIndicators(
                            brakeCylinder: indicators.transform.Find(BrakeCylinderPressure).GetComponent<Indicator>(),
                            equalizingReservoir: indicators.transform.Find(EqualizingReservoirPressure).GetComponent<Indicator>()
                        );
                    });

            public static void Postfix(IndicatorsShunter __instance)
            {
                var state = ExtraBrakeState.Instance(TrainCar.Resolve(__instance.gameObject).brakeSystem);
                var indicators = extraIndicators[__instance];
                indicators.brakeCylinder.value = state.cylinderPressure;
                indicators.equalizingReservoir.value = state.equalizingReservoirPressure;
            }
        }

        [HarmonyPatch(typeof(IndicatorsSteam), nameof(IndicatorsShunter.Update))]
        public static class IndicatorsSteamPatch
        {
            private readonly static Cache<IndicatorsSteam, ExtraIndicators> extraIndicators =
                new Cache<IndicatorsSteam, ExtraIndicators>(
                    indicators =>
                    {
                        // Main.DebugLog(GetPath(indicators));
                        // Main.DebugLog(DumpHierarchy(indicators.gameObject));
                        return new ExtraIndicators(
                            brakeCylinder: indicators.transform.Find(BrakeCylinderPressure).GetComponent<Indicator>(),
                            equalizingReservoir: indicators.transform.Find(EqualizingReservoirPressure).GetComponent<Indicator>()
                        );
                    });

            public static void Postfix(IndicatorsSteam __instance)
            {
                var state = ExtraBrakeState.Instance(TrainCar.Resolve(__instance.gameObject).brakeSystem);
                var indicators = extraIndicators[__instance];
                indicators.brakeCylinder.value = state.cylinderPressure;
                indicators.equalizingReservoir.value = state.equalizingReservoirPressure;
            }
        }

        [HarmonyPatch(typeof(IndicatorsDiesel), nameof(IndicatorsShunter.Update))]
        public static class IndicatorsDieselPatch
        {
            private readonly static Cache<IndicatorsDiesel, ExtraIndicators> extraIndicators =
                new Cache<IndicatorsDiesel, ExtraIndicators>(
                    indicators =>
                    {
                        // Main.DebugLog(GetPath(indicators));
                        // Main.DebugLog(DumpHierarchy(indicators.gameObject));
                        return new ExtraIndicators(
                            brakeCylinder: indicators.transform
                                .Find($"offset/I Indicator meters/{BrakeCylinderPressure}")
                                .GetComponent<Indicator>(),
                            equalizingReservoir: indicators.transform
                                .Find($"offset/I Indicator meters/{EqualizingReservoirPressure}")
                                .GetComponent<Indicator>()
                        );
                    });

            public static void Postfix(IndicatorsDiesel __instance)
            {
                var state = ExtraBrakeState.Instance(TrainCar.Resolve(__instance.gameObject).brakeSystem);
                var indicators = extraIndicators[__instance];
                indicators.brakeCylinder.value = state.cylinderPressure;
                indicators.equalizingReservoir.value = state.equalizingReservoirPressure;
            }
        }
    }
}