using UnityEngine;

namespace DvMod.AirBrake
{
    static public class HandBrake
    {
        const float Increment = 0.05f;
        public static void Update()
        {
            var car = PlayerManager.Car;
            if (car == null || car.brakeSystem.hasIndependentBrake)
                return;
            
            if (KeyBindings.increaseIndependentBrakeKeys.IsPressed())
                car.brakeSystem.independentBrakePosition =
                    Mathf.Clamp01(car.brakeSystem.independentBrakePosition + Increment);
            if (KeyBindings.decreaseIndependentBrakeKeys.IsPressed())
                car.brakeSystem.independentBrakePosition =
                    Mathf.Clamp01(car.brakeSystem.independentBrakePosition - Increment);
        }
    }
}