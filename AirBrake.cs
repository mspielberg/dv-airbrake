using DV.Simulation.Brake;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityModManagerNet;

namespace DvMod.AirBrake
{
    [EnableReloading]
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
            [Draw("Brake application speed")] public float applySpeed = 10f;
            [Draw("Brake release speed")] public float releaseSpeed = 0.5f;
            [Draw("Brake recharge speed")] public float chargeSpeed = 1f;

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
            /*
            /// <returns>Rate of change of p1 normalized to units/s</returns>
            static float EqualizePressure(float dt, ref float p1, ref float p2, float v1, float v2, float multiplier = 1f, float speed = 1f)
            {
                var before = p1;
                Brakeset.EqualizePressure(ref p1, ref p2, v1, v2, multiplier * BrakeSystemConsts.EQUALIZATION_SPEED_MULTIPLIER, speed * BrakeSystemConsts.EQUALIZATION_SPEED_LIMIT, dt);
                return (p1 - before) / dt;
            }

            static void VentFromAngleCocks(Brakeset brakeset, float dt)
            {
                HoseAndCock endmost1 = brakeset.GetEndmost(true);
                HoseAndCock endmost2 = brakeset.GetEndmost(false);
                bool openToAtmosphere1 = endmost1.IsOpenToAtmosphere;
                bool openToAtmosphere2 = endmost2.IsOpenToAtmosphere;
                float rate = 0f;
                if (openToAtmosphere1 | openToAtmosphere2)
                {
                    float atmospheric = 0f;
                    rate = EqualizePressure(dt, ref brakeset.pipePressure, ref atmospheric, brakeset.pipeVolume, BrakeSystemConsts.ATMOSPHERE_VOLUME) / BrakeSystemConsts.MAX_BRAKE_PIPE_PRESSURE;
                }
                endmost1.exhaustFlow = Mathf.SmoothDamp(endmost1.exhaustFlow, openToAtmosphere1 ? rate : 0f, ref endmost1.exhaustFlowRef, 0.1f);
                endmost2.exhaustFlow = Mathf.SmoothDamp(endmost2.exhaustFlow, openToAtmosphere2 ? rate : 0f, ref endmost2.exhaustFlowRef, 0.1f);
            }

            static void VentFromTrainBrakeValves(Brakeset brakeset, float dt)
            {
                var maxBrakePosition = 0f;
                foreach (var loco in brakeset.cars.Where(c => c.hasCompressor))
                {
                    var brakePosition = loco.trainBrakePosition;
                    maxBrakePosition = Mathf.Max(maxBrakePosition, brakePosition);
                    loco.pipeExhaustFlow = Mathf.Max(0f, brakePosition - brakeset.pipePressure / BrakeSystemConsts.MAX_BRAKE_PIPE_PRESSURE);
                }
                var ventTarget = BrakeSystemConsts.MAX_BRAKE_PIPE_PRESSURE * (1f - maxBrakePosition);
                if (ventTarget < brakeset.pipePressure)
                {
                    float atmospheric = 0f;
                    EqualizePressure(dt, ref brakeset.pipePressure, ref atmospheric, brakeset.pipeVolume, BrakeSystemConsts.ATMOSPHERE_VOLUME, 1f, maxBrakePosition);
                }
            }

            static void RechargeMainReservoirs(BrakeSystem car, float dt)
            {
                if (car.hasCompressor)
                {
                    if (car.compressorRunning && car.compressorProductionRate > 0.01f)
                    {
                        car.mainReservoirPressure = car.mainReservoirPressureUnsmoothed =
                            Mathf.Clamp(car.mainReservoirPressure + car.compressorProductionRate * dt, 1f, BrakeSystemConsts.MAX_MAIN_RES_PRESSURE);
                    }
                }
            }

            static void SimulateIndependentBrakes(BrakeSystem car, float dt)
            {
                if (!car.hasIndependentBrake)
                    return;

                car.independentBrakePositionSmoothed = Mathf.SmoothDamp(car.independentBrakePositionSmoothed, car.independentBrakePosition, ref car.independentBrakePositionRef, 0.2f);
                if (car.hasCompressor)
                {
                    float p2 = Mathf.Lerp(4f, 1f, car.independentBrakePositionSmoothed);
                    bool flag2 = (double) p2 < (double) car.independentPipePressure;
                    if ((double) car.mainReservoirPressureUnsmoothed > (double) car.independentPipePressure && !flag2)
                    {
                        float pressureUnsmoothed = car.mainReservoirPressureUnsmoothed;
                        Brakeset.EqualizePressure(ref pressureUnsmoothed, ref car.independentPipePressure, 4f, 2f, (float) (0.399999976158142 * (1.0 - (double) car.independentBrakePositionSmoothed)), 20f, dt);
                        car.pipeExhaustFlow += Mathf.Abs(pressureUnsmoothed - car.mainReservoirPressureUnsmoothed) / (dt * 4f);
                        car.mainReservoirPressureUnsmoothed = pressureUnsmoothed;
                    }
                    if (flag2)
                    {
                        float independentPipePressure = car.independentPipePressure;
                        Brakeset.EqualizePressure(ref independentPipePressure, ref p2, allSet.pipeVolume, 20f, 0.4f, 20f, dt);
                        target3 += Mathf.Abs(car.independentPipePressure - independentPipePressure) / (dt * 4f);
                        car.independentPipePressure = independentPipePressure;
                    }
                    car.independentBrakingFactor = Mathf.InverseLerp(4f, 1f, car.independentPipePressure);
                }
                else
                    car.independentBrakingFactor = Mathf.SmoothDamp(car.independentBrakingFactor, car.independentBrakePositionSmoothed, ref car.independentBrakingFactorRef, 2f);
            }
            */

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

            /*
            static void Update(Brakeset brakeset, float dt)
            {
                VentFromAngleCocks(brakeset, dt);
                VentFromTrainBrakeValves(brakeset, dt);
                foreach (var car in brakeset.cars)
                {
                    RechargeMainReservoir(car, dt);
                    SimulateIndependentBrake(car, dt);
                    ApplyBrakingForce(car, dt);
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
            */

            static bool AttachedToCurrentTrainset(Brakeset brakeset)
            {
                return PlayerManager.Car?.brakeSystem.brakeset == brakeset;
            }

            static float GetSignalPressure(BrakeSystem car)
            {
                if (car.hasIndependentBrake && car.hasCompressor) // locomotive
                    return Mathf.Min(car.brakeset.pipePressure, Mathf.Lerp(car.mainReservoirPressureUnsmoothed, 1f, car.independentBrakePosition));
                return car.brakeset.pipePressure;
            }

            const float BRAKE_CYLINDER_VOLUME = 1f;
            const float CAR_RESERVOIR_VOLUME = 2.5f;
            const float MAX_CYLINDER_PRESSURE = (BrakeSystemConsts.MAX_BRAKE_PIPE_PRESSURE * CAR_RESERVOIR_VOLUME + BRAKE_CYLINDER_VOLUME) / (CAR_RESERVOIR_VOLUME + BRAKE_CYLINDER_VOLUME);
            const float EPSILON = 0.05f;

            const float APPLY_SPEED = 8f;
            const float RELEASE_SPEED = 0.5f;
            const float THRESHOLD_PRESSURE = 1f /* atmospheric */ + 0.5f /* spring */;

            // static Dictionary<BrakeSystem, float> wheelCylinderPressures = new Dictionary<BrakeSystem, float>();

            static void EqualizeWheelCylinder(float dt, BrakeSystem car, ref float otherPressure, float otherVolume, float speed)
            {
                // float cylinderPressure = wheelCylinderPressures[car];
                // Brakeset.EqualizePressure(ref cylinderPressure, ref otherPressure, BRAKE_CYLINDER_VOLUME, otherVolume, BrakeSystemConsts.EQUALIZATION_SPEED_MULTIPLIER * speed, BrakeSystemConsts.EQUALIZATION_SPEED_LIMIT, dt);
                // wheelCylinderPressures[car] = cylinderPressure;
                Brakeset.EqualizePressure(
                    ref car.independentPipePressure,
                    ref otherPressure,
                    BRAKE_CYLINDER_VOLUME,
                    otherVolume,
                    BrakeSystemConsts.EQUALIZATION_SPEED_MULTIPLIER * speed,
                    BrakeSystemConsts.EQUALIZATION_SPEED_LIMIT,
                    dt);
            }

            static void Postfix(float dt)
            {
                foreach (var brakeset in Brakeset.allSets)
                {
                    if (AttachedToCurrentTrainset(brakeset))
                        Main.DebugLog($"Updating brakeset with {brakeset.cars.Count()} cars at {Time.time}.");
                    foreach (var car in brakeset.cars.Where(c => !c.hasCompressor))
                    {
                        // if (AttachedToCurrentTrainset(brakeset))
                        //     Main.DebugLog($"before: pipe={brakeset.pipePressure},pipeVolume={brakeset.pipeVolume},reservoir={car.mainReservoirPressureUnsmoothed},cylinder={car.independentPipePressure}");
                        // if (!wheelCylinderPressures.ContainsKey(car))
                        //     wheelCylinderPressures[car] = 1f;
                        string mode;
                        // var signalPressure = GetSignalPressure(car);
                        var signalPressure = brakeset.pipePressure;
                        if (signalPressure > car.mainReservoirPressureUnsmoothed + EPSILON)
                        {
                            mode = "RELEASE/CHARGE";
                            // RELEASE
                            float atmosphere = 1f;
                            EqualizeWheelCylinder(
                                dt,
                                car,
                                ref atmosphere,
                                BrakeSystemConsts.ATMOSPHERE_VOLUME,
                                Main.settings.releaseSpeed);

                            // CHARGE
                            Brakeset.EqualizePressure(
                                ref car.mainReservoirPressureUnsmoothed,
                                ref brakeset.pipePressure,
                                CAR_RESERVOIR_VOLUME,
                                brakeset.pipeVolume,
                                BrakeSystemConsts.EQUALIZATION_SPEED_MULTIPLIER * Main.settings.chargeSpeed,
                                BrakeSystemConsts.EQUALIZATION_SPEED_LIMIT,
                                dt);
                        }
                        else if (signalPressure < car.mainReservoirPressureUnsmoothed - EPSILON)
                        {
                            mode = "APPLY";
                            // APPLY
                            EqualizeWheelCylinder(
                                dt,
                                car,
                                ref car.mainReservoirPressureUnsmoothed,
                                CAR_RESERVOIR_VOLUME,
                                Main.settings.applySpeed);
                        }
                        else
                        {
                            mode = "LAP";
                            // LAP
                        }
                        var cylinderPressure = car.independentPipePressure;
                        var cylinderBrakingFactor = Mathf.InverseLerp(THRESHOLD_PRESSURE, MAX_CYLINDER_PRESSURE, cylinderPressure);
                        var mechanicalBrakingFactor = (car.hasIndependentBrake && !car.hasCompressor) ? car.independentBrakePosition : 0f; // Caboose only
                        car.brakingFactor = Mathf.Max(mechanicalBrakingFactor, cylinderBrakingFactor);
                        if (AttachedToCurrentTrainset(brakeset))
                            Main.DebugLog($"after: signal={signalPressure},pipe={brakeset.pipePressure},pipeVolume={brakeset.pipeVolume},reservoir={car.mainReservoirPressureUnsmoothed},mode={mode},cylinder={cylinderPressure},mechFactor={mechanicalBrakingFactor},cylFactor={cylinderBrakingFactor},brakingFactor={car.brakingFactor}");
                    }
                }
            }
        }
    }
}
