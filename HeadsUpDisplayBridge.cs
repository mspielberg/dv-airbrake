using DvMod.HeadsUpDisplay;
using System;
using System.Linq;
using UnityModManagerNet;

namespace DvMod.AirBrake
{
    internal class HeadsUpDisplayBridge
    {
        public static HeadsUpDisplayBridge? instance;

        static HeadsUpDisplayBridge()
        {
            try
            {
                if (UnityModManager.FindMod("HeadsUpDisplay") == null)
                    return;
                instance = new HeadsUpDisplayBridge();
                instance.Register();
            }
            catch (System.IO.FileNotFoundException)
            {
            }
        }

        private void Register()
        {
            PushProvider auxReservoirPressureProvider = new PushProvider(
                "Aux reservoir", () => true, v => $"{v:F2} bar");
            foreach (var nonLocoType in Enum.GetValues(typeof(TrainCarType)).OfType<TrainCarType>().Where(t => !CarTypes.IsLocomotive(t)))
                Registry.Register(nonLocoType, auxReservoirPressureProvider);

            PushProvider brakeCylinderPressureProvider = new PushProvider(
                "Brake cylinder", () => true, v => $"{v:F2} bar");
            Registry.Register(RegistryKeys.AllCars, brakeCylinderPressureProvider);

            PushProvider equalizationReservoirPressureProvider = new PushProvider(
                "Equalizing reservoir", () => true, v => $"{v:F2} bar");
            foreach (var locoType in CarTypes.locomotivesMap)
                Registry.Register(locoType, equalizationReservoirPressureProvider);
        }

        public void UpdateAuxReservoirPressure(TrainCar car, float pressure)
        {
            if (Registry.GetProvider(TrainCarType.NotSet, "Aux reservoir") is PushProvider pp)
                pp.MixSmoothedValue(car, pressure);
        }

        public void UpdateBrakeCylinderPressure(TrainCar car, float brakeCylinderPressure)
        {
            if (Registry.GetProvider(TrainCarType.NotSet, "Brake cylinder") is PushProvider pp)
                pp.MixSmoothedValue(car, brakeCylinderPressure);
        }

        public void UpdateEqualizingReservoirPressure(TrainCar car, float equalizingReservoirPressure)
        {
            if (Registry.GetProvider(car.carType, "Equalizing reservoir") is PushProvider pp)
                pp.MixSmoothedValue(car, equalizingReservoirPressure);
        }
    }
}
