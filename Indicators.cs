using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DvMod.AirBrake
{
    public static class Indicators
    {
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
            // Main.DebugLog($"gauge = {gauge}");
            var needle = gauge.transform.GetChild(0);
            // Main.DebugLog($"needle = {needle}");
            // Main.DebugLog(DumpHierarchy(gauge.gameObject));
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

        private static Indicator ModifyGauge(Indicator needle)
        {
            needle.maxValue = Constants.PressureGaugeMax;
            var redNeedle = Object.Instantiate(needle, needle.transform.parent);
            redNeedle.name = needle.name + " red";
            ChangeToRedNeedle(redNeedle);
            return redNeedle;
        }

        private static void InitializeGauges(GameObject interior)
        {
            if (interior == null)
                return;
            switch (TrainCar.Resolve(interior).carType)
            {
                case TrainCarType.LocoDiesel:
                {
                    var indicators = interior.GetComponent<IndicatorsDiesel>();
                    ModifyGauge(indicators.brakePipe);
                    indicators.brakeAux = ModifyGauge(indicators.brakeAux);
                    break;
                }
                case TrainCarType.LocoShunter:
                {
                    var indicators = interior.GetComponent<IndicatorsShunter>();
                    ModifyGauge(indicators.brakePipe);
                    indicators.brakeAux = ModifyGauge(indicators.brakeAux);
                    break;
                }
                case TrainCarType.LocoSteamHeavy:
                {
                    var indicators = interior.GetComponent<IndicatorsSteam>();
                    indicators.brakeAux = ModifyGauge(indicators.brakeAux);
                    ModifyGauge(indicators.brakePipe);
                    break;
                }
            }
        }

        [HarmonyPatch(typeof(TrainCar), nameof(TrainCar.Awake))]
        public static class TrainCarAwakePatch
        {
            public static void Postfix(TrainCar __instance)
            {
                var system = __instance.brakeSystem;

                if (system.brakePipePressure == 1f &&
                    system.mainReservoirPressure == 1f &&
                    system.mainReservoirPressureUnsmoothed == 1f)
                {
                    system.brakePipePressure = system.mainReservoirPressure = system.mainReservoirPressureUnsmoothed = 0f;
                }

                __instance.InteriorPrefabLoaded += InitializeGauges;
            }
        }

        private readonly struct ExtraIndicators
        {
            public readonly Indicator brakeCylinder;
            public readonly Indicator equalizingReservoir;
            public readonly Indicator? airflow;

            public ExtraIndicators(Indicator brakeCylinder, Indicator equalizingReservoir, Indicator? airflow = null)
            {
                this.brakeCylinder = brakeCylinder;
                this.equalizingReservoir = equalizingReservoir;
                this.airflow = airflow;
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
                            brakeCylinder: indicators.brakePipe.transform.parent.Find("I brake_pipe_meter red").GetComponent<Indicator>(),
                            equalizingReservoir: indicators.brakeAux.transform.parent.Find("I brake_aux_res_meter").GetComponent<Indicator>()
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
                            brakeCylinder: indicators.brakePipe.transform.parent.Find("I brake needle pipe red").GetComponent<Indicator>(),
                            equalizingReservoir: indicators.brakeAux.transform.parent.Find("I brake needle aux").GetComponent<Indicator>()
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
                            brakeCylinder: indicators.brakePipe.transform.parent.Find("I brake_aux_meter red").GetComponent<Indicator>(),
                            equalizingReservoir: indicators.brakeAux.transform.parent.Find("I brake_res_meter").GetComponent<Indicator>(),
                            airflow: indicators.brakeAux.transform.parent.Find("I ind_brake_aux_meter").GetComponent<Indicator>()
                        );
                    });

            public static void Postfix(IndicatorsDiesel __instance)
            {
                var car = TrainCar.Resolve(__instance.gameObject);
                var state = ExtraBrakeState.Instance(car.brakeSystem);
                var indicators = extraIndicators[__instance];
                indicators.brakeCylinder.value = state.cylinderPressure;
                indicators.equalizingReservoir.value = state.equalizingReservoirPressure;
                indicators.airflow!.value = state.brakePipeRechargeFlowSmoothed * indicators.airflow!.maxValue / 2f;
            }
        }
    }
}