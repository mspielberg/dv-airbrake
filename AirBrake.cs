using DV.Simulation.Brake;
using DvMod.AirBrake.Components;
using HarmonyLib;
using System.Linq;
using UnityEngine;

namespace DvMod.AirBrake
{
    static internal class Constants
    {
        public const float ApplicationThreshold = 0.05f;

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
    }

    public static class BrakeSystemExtensions
    {
        private static readonly Cache<BrakeSystem, TrainCar> trainCars =
            new Cache<BrakeSystem, TrainCar>(bs => TrainCar.Resolve(bs.gameObject));
        public static TrainCar GetTrainCar(this BrakeSystem brakeSystem) => trainCars[brakeSystem];

        public static ref float CylinderPressure(this BrakeSystem brakeSystem) =>
            ref ExtraBrakeState.Instance(brakeSystem).cylinderPressure;
    }

    internal static class AirSystem
    {
        /// <returns>Rate of change of p1 normalized to units/s</returns>
        public static float Equalize(float dt, ref float p1, ref float p2, float v1, float v2, float multiplier = 1f, float limit = 1f)
        {
            var before = p1;
            Brakeset.EqualizePressure(ref p1, ref p2, v1, v2, multiplier * BrakeSystemConsts.EQUALIZATION_SPEED_MULTIPLIER, limit * BrakeSystemConsts.EQUALIZATION_SPEED_LIMIT, dt);
            return dt == 0f ? 0f : (p1 - before) / dt;
        }

        public static float OneWayFlow(float dt, ref float fromPressure, ref float toPressure, float fromVolume, float toVolume, float speed = 1f, float limit = 1f)
        {
            if (fromPressure <= toPressure)
                return 0f;
            return Equalize(dt, ref toPressure, ref fromPressure, toVolume, fromVolume, speed, limit);
        }

        public static float Vent(float dt, ref float pressure, float volume, float speed = 1f, float limit = 1f)
        {
            float atmosphere = 0f;
            return OneWayFlow(dt, ref pressure, ref atmosphere, volume, BrakeSystemConsts.ATMOSPHERE_VOLUME, speed, limit);
        }
    }

    public static class AirBrake
    {
        [HarmonyPatch(typeof(Brakeset), nameof(Brakeset.Update))]
        static class BrakesetUpdatePatch
        {

            /* original decompiled code */
            /*
            foreach (Brakeset allSet in Brakeset.allSets)
            {
                HoseAndCock endmost1 = allSet.GetEndmost(true);
                HoseAndCock endmost2 = allSet.GetEndmost(false);
                bool openToAtmosphere1 = endmost1.IsOpenToAtmosphere;
                bool openToAtmosphere2 = endmost2.IsOpenToAtmosphere;
                float exhaustFlow1 = 0.0f;
                float exhaustFlow2 = 0.0f;
                if (openToAtmosphere1 | openToAtmosphere2)
                {
                    float p2 = 1f;
                    float pipePressure = allSet.pipePressure;
                    Brakeset.EqualizePressure(ref pipePressure, ref p2, allSet.pipeVolume, 20f, 0.02f, 1f, dt);
                    float num = Mathf.Abs(pipePressure - allSet.pipePressure) / (dt * 4f);
                    allSet.pipePressure = pipePressure;
                    if (openToAtmosphere1 & openToAtmosphere2)
                        exhaustFlow1 = exhaustFlow2 = num * 0.5f;
                    else if (openToAtmosphere1)
                        exhaustFlow1 = num;
                    else
                        exhaustFlow2 = num;
                }
                endmost1.exhaustFlow = Mathf.SmoothDamp(endmost1.exhaustFlow, exhaustFlow1, ref endmost1.exhaustFlowRef, 0.1f);
                endmost2.exhaustFlow = Mathf.SmoothDamp(endmost2.exhaustFlow, exhaustFlow2, ref endmost2.exhaustFlowRef, 0.1f);
                bool flag1 = (double) allSet.targetPipePressure < (double) allSet.pipePressure;
                float pipeExhaustFlow = 0.0f;
                if (flag1)
                {
                    float pipePressure = allSet.pipePressure;
                    float p2 = 1f;
                    Brakeset.EqualizePressure(ref pipePressure, ref p2, allSet.pipeVolume, 20f, 0.02f, 1f, dt);
                    pipeExhaustFlow = Mathf.Abs(allSet.pipePressure - pipePressure) / (dt * 4f);
                    allSet.pipePressure = pipePressure;
                }
                float b = 4f;
                foreach (BrakeSystem car in allSet.cars)
                {
                    float resToPipeFlow = 0.0f;
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
                            resToPipeFlow += Mathf.Abs(pressureUnsmoothed - car.mainReservoirPressureUnsmoothed) / (dt * 4f);
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
                                resToPipeFlow += Mathf.Abs(pressureUnsmoothed - car.mainReservoirPressureUnsmoothed) / (dt * 4f);
                                car.mainReservoirPressureUnsmoothed = pressureUnsmoothed;
                            }
                            if (flag2)
                            {
                                float independentPipePressure = car.independentPipePressure;
                                Brakeset.EqualizePressure(ref independentPipePressure, ref p2, allSet.pipeVolume, 20f, 0.4f, 20f, dt);
                                pipeExhaustFlow += Mathf.Abs(car.independentPipePressure - independentPipePressure) / (dt * 4f);
                                car.independentPipePressure = independentPipePressure;
                            }
                            if ((double) car.brakePipePressure > (double) car.mainReservoirPressureUnsmoothed && !flag2)
                            {
                                float pressureUnsmoothed = car.mainReservoirPressureUnsmoothed;
                                Brakeset.EqualizePressure(ref pressureUnsmoothed, ref car.brakePipePressure, 4f, 2f, 0.4f, 20f, dt);
                                resToPipeFlow += Mathf.Abs(pressureUnsmoothed - car.mainReservoirPressureUnsmoothed) / (dt * 4f);
                                car.mainReservoirPressureUnsmoothed = pressureUnsmoothed;
                            }
                            car.independentBrakingFactor = Mathf.InverseLerp(4f, 1f, car.independentPipePressure);
                        }
                        else
                            car.independentBrakingFactor = Mathf.SmoothDamp(car.independentBrakingFactor, car.independentBrakePositionSmoothed, ref car.independentBrakingFactorRef, 2f);
                    }
                    car.mainReservoirPressure = Mathf.SmoothDamp(car.mainReservoirPressure, car.mainReservoirPressureUnsmoothed, ref car.mainResPressureRef, 0.8f);
                    car.mainResToPipeFlow = Mathf.SmoothDamp(car.mainResToPipeFlow, resToPipeFlow, ref car.mainResToPipeFlowRef, 0.2f);
                    car.pipeExhaustFlow = Mathf.SmoothDamp(car.pipeExhaustFlow,pipeExhaustFlow , ref car.pipeExhaustFlowRef, 0.2f);
                    double independentBrakingFactor = (double) car.independentBrakingFactor;
                    float target5 = Mathf.Max((float) num1, (float) independentBrakingFactor);
                    car.brakingFactor = Mathf.SmoothDamp(car.brakingFactor, target5, ref car.brakingFactorRef, 0.2f);
                    car.Front.UpdatePressurized();
                    car.Rear.UpdatePressurized();
                }
                allSet.targetPipePressure = b;
            }
            */

            private static void RechargeMainReservoir(BrakeSystem car, float dt)
            {
                if (car.compressorRunning)
                {
                    var increase = car.compressorProductionRate * Main.settings.compressorSpeed * dt;
                    car.mainReservoirPressureUnsmoothed =
                         Mathf.Clamp(car.mainReservoirPressureUnsmoothed + increase, 0f, BrakeSystemConsts.MAX_MAIN_RES_PRESSURE);
                }
                car.mainReservoirPressure = Mathf.SmoothDamp(car.mainReservoirPressure, car.mainReservoirPressureUnsmoothed, ref car.mainResPressureRef, 0.8f);
            }

            private const float EqualizationSpeed = 100f;
            private static void EqualizeBrakePipe(Brakeset brakeset, float dt)
            {
                var cars = brakeset.cars.AsReadOnly();
                foreach (var (a, b) in cars.Zip(cars.Skip(1), (a, b) => (a, b)))
                {
                    AirBrake.DebugLog(a, $"EqualizeBrakePipe: a={a.brakePipePressure}, b={b.brakePipePressure}");
                    AirSystem.Equalize(
                        dt,
                        ref a.brakePipePressure,
                        ref b.brakePipePressure,
                        BrakeSystemConsts.PIPE_VOLUME,
                        BrakeSystemConsts.PIPE_VOLUME,
                        EqualizationSpeed,
                        EqualizationSpeed
                    );
                }
                brakeset.firstCar.Front.UpdatePressurized();
                brakeset.lastCar.Rear.UpdatePressurized();
            }

            private static float GetMechanicalBrakeFactor(BrakeSystem car)
            {
                if (car.hasIndependentBrake && !car.hasCompressor) // Caboose only
                    return car.independentBrakePosition;
                return 0f;
            }

            private static void ApplyBrakingForce(BrakeSystem car)
            {
                var state = ExtraBrakeState.Instance(car);
                var cylinderBrakingFactor = Mathf.InverseLerp(Constants.THRESHOLD_PRESSURE, Constants.MAX_CYLINDER_PRESSURE, state.cylinderPressure);
                var mechanicalBrakingFactor = GetMechanicalBrakeFactor(car);
                car.brakingFactor = Mathf.Max(mechanicalBrakingFactor, cylinderBrakingFactor);
            }

            private static void UpdateHUD(BrakeSystem car)
            {
                var state = ExtraBrakeState.Instance(car);
                HeadsUpDisplayBridge.instance?.UpdateAuxReservoirPressure(car.GetTrainCar(), state.auxReservoirPressure);
                HeadsUpDisplayBridge.instance?.UpdateBrakeCylinderPressure(car.GetTrainCar(), state.cylinderPressure);
            }

            private static void Update(Brakeset brakeset, float dt)
            {
                AngleCocks.Update(brakeset, dt);
                EqualizeBrakePipe(brakeset, dt);
                foreach (var car in brakeset.cars)
                {
                    if (car.hasCompressor)
                    {
                        RechargeMainReservoir(car, dt);
                        BrakeValve26L.Update(car, dt);
                    }
                    else
                    {
                        PlainTripleValve.Update(car, dt);
                    }
                    ApplyBrakingForce(car);
                    UpdateHUD(car);
                }
            }

            public static bool Prefix(float dt)
            {
                if (!Main.enabled)
                    return true;
                foreach (var brakeset in Brakeset.allSets)
                    Update(brakeset, dt);
                return false;
            }
        }

        public static void DebugLog(BrakeSystem car, string msg)
        {
            if (PlayerManager.Car?.brakeSystem == car)
                Main.DebugLog(msg);
        }
    }
}
