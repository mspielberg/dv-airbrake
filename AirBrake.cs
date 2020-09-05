using DV.Simulation.Brake;
using HarmonyLib;
using System;
using UnityModManagerNet;

namespace DvMod.AirBrake
{
    public static class Main
    {
        public static UnityModManager.ModEntry mod;
        public static Settings settings;
        public static bool enabled;

        static bool Load(UnityModManager.ModEntry modEntry)
        {
            mod = modEntry;

            try { settings = Settings.Load<Settings>(modEntry); } catch {}
            var harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll();

            modEntry.OnGUI = OnGui;
            modEntry.OnSaveGUI = OnSaveGui;
            modEntry.OnToggle = OnToggle;
            modEntry.OnUnload = OnUnload;

            return true;
        }

        static void OnGui(UnityModManager.ModEntry modEntry)
        {
            settings.Draw(modEntry);
        }

        static void OnSaveGui(UnityModManager.ModEntry modEntry)
        {
            settings.Save(modEntry);
        }

        static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            if (value != enabled)
            {
                enabled = value;
            }
            return true;
        }

        static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            var harmony = new Harmony(modEntry.Info.Id);
            harmony.UnpatchAll(modEntry.Info.Id);
            return true;
        }

        public static void DebugLog(string message)
        {
            if (settings.enableLogging)
                mod.Logger.Log(message);
        }

        public class Settings : UnityModManager.ModSettings, IDrawable
        {

            [Draw("Enable logging")] public bool enableLogging = false;

            override public void Save(UnityModManager.ModEntry entry) {
                Save<Settings>(this, entry);
            }

            public void OnChange() {
            }
        }
    }

    public static class AirBrake
    {
        [HarmonyPatch(typeof(Brakeset), nameof(Brakeset.Update))]
        static class UpdatePatch
        {
            static void EqualizePressure(float dt, ref float p1, ref float p2, float v1, float v2, float multiplier = 1f, float speed = 1f)
            {
                Brakeset.EqualizePressure(ref p1, ref p2, v1, v2, multiplier * BrakeSystemConsts.EQUALIZATION_SPEED_MULTIPLIER, speed * BrakeSystemConsts.EQUALIZATION_SPEED_LIMIT, dt);
            }

            static void VentToAtmosphere(Brakeset brakeset, float dt)
            {
                HoseAndCock endmost1 = brakeset.GetEndmost(true);
                HoseAndCock endmost2 = brakeset.GetEndmost(false);
                bool openToAtmosphere1 = endmost1.IsOpenToAtmosphere;
                bool openToAtmosphere2 = endmost2.IsOpenToAtmosphere;
                if (openToAtmosphere1 | openToAtmosphere2)
                {
                    float atmospheric = 0f;
                    float prevPressure = brakeset.pipePressure;
                    EqualizePressure(dt, ref brakeset.pipePressure, ref atmospheric, brakeset.pipeVolume, BrakeSystemConsts.ATMOSPHERE_PRESSURE);
                    float rate = Mathf.Abs(newPressure - brakeset.pipePressure) / BrakeSystemConsts.MAX_BRAKE_PIPE_PRESSURE / dt;
                    allSet.pipePressure = newPressure;
                }
                endmost1.exhaustFlow = Mathf.SmoothDamp(endmost1.exhaustFlow, openToAtmosphere1 ? rate : 0f, ref endmost1.exhaustFlowRef, 0.1f);
                endmost2.exhaustFlow = Mathf.SmoothDamp(endmost2.exhaustFlow, openToAtmosphere2 ? rate : 0f, ref endmost2.exhaustFlowRef, 0.1f);
            }

            static void HandleTrainBrakeValve(Brakeset brakeset, float dt)
            {
                var ventTarget = BrakeSystemConsts.MAX_BRAKE_PIPE_PRESSURE;
                foreach (var system in brakeset.cars)
                    if (system.hasCompressor)
                        ventTarget = Mathf.Min(ventTarget, BrakeSystemConsts.MAX_BRAKE_PIPE_PRESSURE * (1f - system.trainBrakePositionUnsmoothed));
                float atmospheric = 0f;
                EqualizePressure(dt, ref brakeset.pipePressure, ref atmospheric, brakeset.pipeVolume, BrakeSystemConsts.ATMOSPHERE_PRESSURE);
            }

            /* original decompiled code */
            /*
            foreach (Brakeset allSet in Brakeset.allSets)
            {
                HoseAndCock endmost1 = allSet.GetEndmost(true);
                HoseAndCock endmost2 = allSet.GetEndmost(false);
                bool openToAtmosphere1 = endmost1.IsOpenToAtmosphere;
                bool openToAtmosphere2 = endmost2.IsOpenToAtmosphere;
                float target1 = 0.0f;
                float target2 = 0.0f;
                if (openToAtmosphere1 | openToAtmosphere2)
                {
                    float p2 = 1f;
                    float pipePressure = allSet.pipePressure;
                    Brakeset.EqualizePressure(ref pipePressure, ref p2, allSet.pipeVolume, 20f, 0.02f, 1f, dt);
                    float num = Mathf.Abs(pipePressure - allSet.pipePressure) / (dt * 4f);
                    allSet.pipePressure = pipePressure;
                    if (openToAtmosphere1 & openToAtmosphere2)
                        target1 = target2 = num * 0.5f;
                    else if (openToAtmosphere1)
                        target1 = num;
                    else
                        target2 = num;
                }
                endmost1.exhaustFlow = Mathf.SmoothDamp(endmost1.exhaustFlow, target1, ref endmost1.exhaustFlowRef, 0.1f);
                endmost2.exhaustFlow = Mathf.SmoothDamp(endmost2.exhaustFlow, target2, ref endmost2.exhaustFlowRef, 0.1f);
                bool flag1 = (double) allSet.targetPipePressure < (double) allSet.pipePressure;
                float target3 = 0.0f;
                if (flag1)
                {
                    float pipePressure = allSet.pipePressure;
                    float p2 = 1f;
                    Brakeset.EqualizePressure(ref pipePressure, ref p2, allSet.pipeVolume, 20f, 0.02f, 1f, dt);
                    target3 = Mathf.Abs(allSet.pipePressure - pipePressure) / (dt * 4f);
                    allSet.pipePressure = pipePressure;
                }
                float b = 4f;
                foreach (BrakeSystem car in allSet.cars)
                {
                    float target4 = 0.0f;
                    if (car.hasCompressor)
                    {
                        if (car.compressorRunning && (double) car.compressorProductionRate > 0.00999999977648258)
                        car.mainReservoirPressureUnsmoothed = Mathf.Clamp(car.mainReservoirPressureUnsmoothed + car.compressorProductionRate * dt, 1f, 4f);
                        car.trainBrakePositionSmoothed = Mathf.SmoothDamp(car.trainBrakePositionSmoothed, car.trainBrakePosition, ref car.trainBrakePositionRef, 0.2f);
                        b = Mathf.Min(Mathf.Lerp(4f, 1f, car.trainBrakePositionSmoothed), b);
                        float num = Mathf.Min(Mathf.Min(car.mainReservoirPressureUnsmoothed, allSet.targetPipePressure), 4f);
                        if ((double) allSet.pipePressure < (double) num && !flag1)
                        {
                        float pressureUnsmoothed = car.mainReservoirPressureUnsmoothed;
                        Brakeset.EqualizePressure(ref pressureUnsmoothed, ref allSet.pipePressure, 4f, allSet.pipeVolume, (float) (0.0199999995529652 * (1.0 - (double) car.trainBrakePositionSmoothed)), 1f, dt);
                        target4 += Mathf.Abs(pressureUnsmoothed - car.mainReservoirPressureUnsmoothed) / (dt * 4f);
                        car.mainReservoirPressureUnsmoothed = pressureUnsmoothed;
                        allSet.pipePressure = Mathf.Clamp(allSet.pipePressure, 1f, allSet.targetPipePressure);
                        }
                    }
                    car.brakePipePressure = Mathf.SmoothDamp(car.brakePipePressure, allSet.pipePressure, ref car.brakePipePressureRef, 0.3f);
                    double num1 = (double) Mathf.InverseLerp(4f, 0.7f, car.brakePipePressure);
                    if (car.hasIndependentBrake)
                    {
                        car.independentBrakePositionSmoothed = Mathf.SmoothDamp(car.independentBrakePositionSmoothed, car.independentBrakePosition, ref car.independentBrakePositionRef, 0.2f);
                        if (car.hasCompressor)
                        {
                        float p2 = Mathf.Lerp(4f, 1f, car.independentBrakePositionSmoothed);
                        bool flag2 = (double) p2 < (double) car.independentPipePressure;
                        if ((double) car.mainReservoirPressureUnsmoothed > (double) car.independentPipePressure && !flag2)
                        {
                            float pressureUnsmoothed = car.mainReservoirPressureUnsmoothed;
                            Brakeset.EqualizePressure(ref pressureUnsmoothed, ref car.independentPipePressure, 4f, 2f, (float) (0.399999976158142 * (1.0 - (double) car.independentBrakePositionSmoothed)), 20f, dt);
                            target4 += Mathf.Abs(pressureUnsmoothed - car.mainReservoirPressureUnsmoothed) / (dt * 4f);
                            car.mainReservoirPressureUnsmoothed = pressureUnsmoothed;
                        }
                        if (flag2)
                        {
                            float independentPipePressure = car.independentPipePressure;
                            Brakeset.EqualizePressure(ref independentPipePressure, ref p2, allSet.pipeVolume, 20f, 0.4f, 20f, dt);
                            target3 += Mathf.Abs(car.independentPipePressure - independentPipePressure) / (dt * 4f);
                            car.independentPipePressure = independentPipePressure;
                        }
                        if ((double) car.brakePipePressure > (double) car.mainReservoirPressureUnsmoothed && !flag2)
                        {
                            float pressureUnsmoothed = car.mainReservoirPressureUnsmoothed;
                            Brakeset.EqualizePressure(ref pressureUnsmoothed, ref car.brakePipePressure, 4f, 2f, 0.4f, 20f, dt);
                            target4 += Mathf.Abs(pressureUnsmoothed - car.mainReservoirPressureUnsmoothed) / (dt * 4f);
                            car.mainReservoirPressureUnsmoothed = pressureUnsmoothed;
                        }
                        car.independentBrakingFactor = Mathf.InverseLerp(4f, 1f, car.independentPipePressure);
                        }
                        else
                        car.independentBrakingFactor = Mathf.SmoothDamp(car.independentBrakingFactor, car.independentBrakePositionSmoothed, ref car.independentBrakingFactorRef, 2f);
                    }
                    car.mainReservoirPressure = Mathf.SmoothDamp(car.mainReservoirPressure, car.mainReservoirPressureUnsmoothed, ref car.mainResPressureRef, 0.8f);
                    car.mainResToPipeFlow = Mathf.SmoothDamp(car.mainResToPipeFlow, target4, ref car.mainResToPipeFlowRef, 0.2f);
                    car.pipeExhaustFlow = Mathf.SmoothDamp(car.pipeExhaustFlow, target3, ref car.pipeExhaustFlowRef, 0.2f);
                    double independentBrakingFactor = (double) car.independentBrakingFactor;
                    float target5 = Mathf.Max((float) num1, (float) independentBrakingFactor);
                    car.brakingFactor = Mathf.SmoothDamp(car.brakingFactor, target5, ref car.brakingFactorRef, 0.2f);
                    car.Front.UpdatePressurized();
                    car.Rear.UpdatePressurized();
                }
                allSet.targetPipePressure = b;
            }
            */
            static void Update(Brakeset brakeset, float dt)
            {
                VentToAtmosphere(brakeset, dt);
                HandleTrainBrakeValve(brakeset, dt);
                {

                }
            }

            static bool Prefix(float dt)
            {
                if (!Main.enabled)
                    return true;
                foreach (var brakeset in Brakeset.allSets)
                    Update(brakeset, dt);
                return false;
            }
        }
    }
}
