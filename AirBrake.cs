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
        public static UnityModManager.ModEntry? mod;
        public static Settings settings = new Settings();
        public static bool enabled;

        static public bool Load(UnityModManager.ModEntry modEntry)
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

        static private void OnGui(UnityModManager.ModEntry modEntry)
        {
            settings.Draw(modEntry);
        }

        static private void OnSaveGui(UnityModManager.ModEntry modEntry)
        {
            settings.Save(modEntry);
        }

        static private bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            if (value != enabled)
            {
                enabled = value;
            }
            return true;
        }

        static private bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            var harmony = new Harmony(modEntry.Info.Id);
            harmony.UnpatchAll(modEntry.Info.Id);
            return true;
        }

        public static void DebugLog(string message)
        {
            if (settings.enableLogging)
                mod?.Logger.Log(message);
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

    static internal class Constants
    {
        public const float BRAKE_CYLINDER_VOLUME = 1f;
        public const float AUX_RESERVOIR_VOLUME = 2.5f;
        public const float MAX_CYLINDER_PRESSURE =
            ((BrakeSystemConsts.MAX_BRAKE_PIPE_PRESSURE * AUX_RESERVOIR_VOLUME) + BRAKE_CYLINDER_VOLUME) / (AUX_RESERVOIR_VOLUME + BRAKE_CYLINDER_VOLUME);
        public const float THRESHOLD_PRESSURE = 1f /* atmospheric */ + 0.5f /* spring */;
    }

    internal class ExtraBrakeState
    {
        public float cylinderPressure = 1f;
        public float auxReservoirPressure = 1f;

        private static readonly Cache<BrakeSystem, ExtraBrakeState> cache = new Cache<BrakeSystem, ExtraBrakeState>(_ => new ExtraBrakeState());
        public static ExtraBrakeState Instance(BrakeSystem system) => cache[system];

        public void EqualizeWheelCylinder(float dt, ref float otherPressure, float otherVolume, float speed = 1f)
        {
            Brakeset.EqualizePressure(
                ref cylinderPressure,
                ref otherPressure,
                Constants.BRAKE_CYLINDER_VOLUME,
                otherVolume,
                BrakeSystemConsts.EQUALIZATION_SPEED_MULTIPLIER * speed,
                BrakeSystemConsts.EQUALIZATION_SPEED_LIMIT,
                dt);
        }
    }

    public static class AirBrake
    {
        [HarmonyPatch(typeof(Brakeset), nameof(Brakeset.Update))]
        private static class UpdatePatch
        {
            private static bool AttachedToCurrentTrainset(Brakeset brakeset)
            {
                return PlayerManager.Car?.brakeSystem.brakeset == brakeset;
            }

            private const float BRAKE_CYLINDER_VOLUME = 1f;
            private const float CAR_RESERVOIR_VOLUME = 2.5f;
            private const float MAX_CYLINDER_PRESSURE =
                ((BrakeSystemConsts.MAX_BRAKE_PIPE_PRESSURE * CAR_RESERVOIR_VOLUME) + BRAKE_CYLINDER_VOLUME) / (CAR_RESERVOIR_VOLUME + BRAKE_CYLINDER_VOLUME);
            private const float THRESHOLD_PRESSURE = 1f /* atmospheric */ + 0.5f /* spring */;

            private static float SimulateLocoBrake(BrakeSystem car, float _)
            {
                var signal = Mathf.Min(car.independentPipePressure, car.brakeset.pipePressure);
                HeadsUpDisplayBridge.instance?.UpdateAuxReservoirPressure(car.GetTrainCar(), car.mainReservoirPressure);
                return Mathf.Lerp(1f, BrakeSystemConsts.MAX_BRAKE_PIPE_PRESSURE, Mathf.InverseLerp(BrakeSystemConsts.MAX_BRAKE_PIPE_PRESSURE, 1f, signal));
            }

            private static float SimulateTripleValve(BrakeSystem car, float dt)
            {
                var brakeset = car.brakeset;
                ExtraBrakeState data = extraBrakeData[car];
                string mode;
                if (brakeset.pipePressure > data.auxReservoirPressure)
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
                else if (brakeset.pipePressure < data.auxReservoirPressure - EPSILON)
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
                HeadsUpDisplayBridge.instance?.UpdateAuxReservoirPressure(car.GetTrainCar(), data.auxReservoirPressure);
                if (AttachedToCurrentTrainset(brakeset))
                    Main.DebugLog($"pipe={brakeset.pipePressure},pipeVolume={brakeset.pipeVolume},reservoir={data.auxReservoirPressure},mode={mode}");
                return data.cylinderPressure;
            }

            public static void Postfix(float dt)
            {
                if (!Main.enabled)
                    return;
                foreach (var brakeset in Brakeset.allSets)
                {
                    if (AttachedToCurrentTrainset(brakeset))
                        Main.DebugLog($"Updating brakeset with {brakeset.cars.Count} cars at {Time.time}.");
                    foreach (var car in brakeset.cars)
                    {
                        var cylinderPressure = car.hasCompressor ? SimulateLocoBrake(car, dt) : SimulateTripleValve(car, dt);
                        HeadsUpDisplayBridge.instance?.UpdateBrakeCylinderPressure(car.GetTrainCar(), cylinderPressure);
                        var cylinderBrakingFactor = Mathf.InverseLerp(THRESHOLD_PRESSURE, MAX_CYLINDER_PRESSURE, cylinderPressure);
                        var mechanicalBrakingFactor = !car.hasCompressor ? car.independentBrakePosition : 0f; // Caboose only
                        car.brakingFactor = Mathf.Max(mechanicalBrakingFactor, cylinderBrakingFactor);
                    }
                }
            }
        }
    }

    public static class BrakeSystemExtensions
    {
        private static readonly Cache<BrakeSystem, TrainCar> trainCars =
            new Cache<BrakeSystem, TrainCar>(bs => TrainCar.Resolve(bs.gameObject));
        public static TrainCar GetTrainCar(this BrakeSystem brakeSystem) => trainCars[brakeSystem];
    }

    public class Cache<TKey, TEntry>
    {
        private readonly Func<TKey, TEntry> generator;
        private readonly Dictionary<TKey, TEntry> cache = new Dictionary<TKey, TEntry>();

        public Cache(Func<TKey, TEntry> generator)
        {
            this.generator = generator;
        }

        public TEntry this[TKey key]
        {
            get {
                if (cache.TryGetValue(key, out TEntry entry))
                    return entry;
                entry = generator(key);
                cache[key] = entry;
                return entry;
            }
        }
    }
}
