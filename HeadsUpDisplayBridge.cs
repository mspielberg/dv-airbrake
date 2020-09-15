using DvMod.HeadsUpDisplay;
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
                "Aux reservoir pressure", () => true, v => v.ToString("F2"));
            Registry.Register(RegistryKeys.AllCars, auxReservoirPressureProvider);
            PushProvider brakeCylinderPressureProvider = new PushProvider(
                "Brake cylinder pressure", () => true, v => v.ToString("F2"));
            Registry.Register(RegistryKeys.AllCars, brakeCylinderPressureProvider);

            PushProvider equalizationReservoirPressureProvider = new PushProvider(
                "Equalizing reservoir pressure", () => true, v => v.ToString("F2"));
            Registry.Register(TrainCarType.LocoShunter, equalizationReservoirPressureProvider);
        }

        public void UpdateAuxReservoirPressure(TrainCar car, float pressure)
        {
            if (Registry.GetProvider(TrainCarType.NotSet, "Aux reservoir pressure") is PushProvider pp)
                pp.MixSmoothedValue(car, pressure);
        }

        public void UpdateBrakeCylinderPressure(TrainCar car, float brakeCylinderPressure)
        {
            if (Registry.GetProvider(TrainCarType.NotSet, "Brake cylinder pressure") is PushProvider pp)
                pp.MixSmoothedValue(car, brakeCylinderPressure);
        }

        public void UpdateEqualizingReservoirPressure(TrainCar car, float equalizingReservoirPressure)
        {
            if (Registry.GetProvider(car.carType, "Equalizing reservoir pressure") is PushProvider pp)
                pp.MixSmoothedValue(car, equalizingReservoirPressure);
        }
    }
}
