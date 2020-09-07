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
            [Draw("Brake release speed")] public float releaseSpeed = 0.25f;
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
            static bool AttachedToCurrentTrainset(Brakeset brakeset)
            {
                return PlayerManager.Car?.brakeSystem.brakeset == brakeset;
            }

            const float BRAKE_CYLINDER_VOLUME = 1f;
            const float CAR_RESERVOIR_VOLUME = 2.5f;
            const float MAX_CYLINDER_PRESSURE = (BrakeSystemConsts.MAX_BRAKE_PIPE_PRESSURE * CAR_RESERVOIR_VOLUME + BRAKE_CYLINDER_VOLUME) / (CAR_RESERVOIR_VOLUME + BRAKE_CYLINDER_VOLUME);
            const float EPSILON = 0.05f;

            const float APPLY_SPEED = 8f;
            const float RELEASE_SPEED = 0.5f;
            const float THRESHOLD_PRESSURE = 1f /* atmospheric */ + 0.5f /* spring */;

            class ExtraBrakeData
            {
                public float cylinderPressure = 1f;
                public float auxReservoirPressure = 1f;
            }

            static Dictionary<BrakeSystem, ExtraBrakeData> extraBrakeData = new Dictionary<BrakeSystem, ExtraBrakeData>();

            static void EqualizeWheelCylinder(float dt, BrakeSystem car, ref float otherPressure, float otherVolume, float speed)
            {
                var data = extraBrakeData[car];
                Main.DebugLog($"Calling EP between {data.cylinderPressure} and {otherPressure}");
                Brakeset.EqualizePressure(
                    ref data.cylinderPressure,
                    ref otherPressure,
                    BRAKE_CYLINDER_VOLUME,
                    otherVolume,
                    BrakeSystemConsts.EQUALIZATION_SPEED_MULTIPLIER * speed,
                    BrakeSystemConsts.EQUALIZATION_SPEED_LIMIT,
                    dt);
                Main.DebugLog($"after: {data.cylinderPressure} and {otherPressure}");
            }

            static void Postfix(float dt)
            {
                foreach (var brakeset in Brakeset.allSets)
                {
                    if (AttachedToCurrentTrainset(brakeset))
                        Main.DebugLog($"Updating brakeset with {brakeset.cars.Count()} cars at {Time.time}.");
                    foreach (var car in brakeset.cars)//.Where(c => !c.hasCompressor))
                    {
                        ExtraBrakeData data;
                        if (!extraBrakeData.TryGetValue(car, out data))
                            extraBrakeData[car] = data = new ExtraBrakeData();

                        string mode;
                        var signalPressure = brakeset.pipePressure;
                        if (signalPressure > data.auxReservoirPressure + EPSILON)
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
                                ref data.auxReservoirPressure,
                                ref brakeset.pipePressure,
                                CAR_RESERVOIR_VOLUME,
                                brakeset.pipeVolume,
                                BrakeSystemConsts.EQUALIZATION_SPEED_MULTIPLIER * Main.settings.chargeSpeed,
                                BrakeSystemConsts.EQUALIZATION_SPEED_LIMIT,
                                dt);
                        }
                        else if (signalPressure < data.auxReservoirPressure - EPSILON)
                        {
                            mode = "APPLY";
                            // APPLY
                            EqualizeWheelCylinder(
                                dt,
                                car,
                                ref data.auxReservoirPressure,
                                CAR_RESERVOIR_VOLUME,
                                Main.settings.applySpeed);
                        }
                        else
                        {
                            mode = "LAP";
                            // LAP
                        }

                        var cylinderPressure = data.cylinderPressure;
                        if (car.hasCompressor)
                            cylinderPressure = Mathf.Max(
                                cylinderPressure,
                                Mathf.Min(car.mainReservoirPressureUnsmoothed, BrakeSystemConsts.MAX_BRAKE_PIPE_PRESSURE * car.independentBrakePosition));
                        var cylinderBrakingFactor = Mathf.InverseLerp(THRESHOLD_PRESSURE, MAX_CYLINDER_PRESSURE, cylinderPressure);
                        var mechanicalBrakingFactor = (car.hasIndependentBrake && !car.hasCompressor) ? car.independentBrakePosition : 0f; // Caboose only
                        car.brakingFactor = Mathf.Max(mechanicalBrakingFactor, cylinderBrakingFactor);
                        if (AttachedToCurrentTrainset(brakeset))
                            Main.DebugLog($"after: signal={signalPressure},pipe={brakeset.pipePressure},pipeVolume={brakeset.pipeVolume},reservoir={data.auxReservoirPressure},mode={mode},cylinder={cylinderPressure},mechFactor={mechanicalBrakingFactor},cylFactor={cylinderBrakingFactor},brakingFactor={car.brakingFactor}");
                    }
                }
            }
        }
    }
}
