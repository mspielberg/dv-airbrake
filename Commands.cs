using CommandTerminal;
using HarmonyLib;
using System;
using System.Linq;
using UnityEngine;

namespace DvMod.AirBrake
{
    public static class Commands
    {
        [HarmonyPatch(typeof(Terminal), nameof(Terminal.Start))]
        public static class RegisterCommandsPatch
        {
            public static void Postfix()
            {
                Register();
            }
        }

        private static void Register(string name, Action<CommandArg[]> proc)
        {
            if (Terminal.Shell == null)
                return;
            if (Terminal.Shell.Commands.Remove(name.ToUpper()))
                Main.DebugLog($"replacing existing command {name}");
            else
                Terminal.Autocomplete.Register(name);
            Terminal.Shell.AddCommand(name, proc);
        }

        public static void Register()
        {
            Register("airbrake.fillTrain", _ =>
            {
                if (PlayerManager.Car != null)
                {
                    foreach (var car in PlayerManager.Car.trainset.cars)
                    {
                        var brakeSystem = car.brakeSystem;
                        var state = ExtraBrakeState.Instance(brakeSystem);
                        brakeSystem.brakePipePressure = state.brakePipePressureUnsmoothed = Constants.MaxBrakePipePressure;
                        var carType = car.carType;
                        if (CarTypes.IsLocomotive(carType))
                        {
                            brakeSystem.mainReservoirPressure = brakeSystem.mainReservoirPressureUnsmoothed =
                                Constants.MaxMainReservoirPressure;
                            state.equalizingReservoirPressure = Constants.MaxBrakePipePressure;
                        }
                        else
                        {
                            state.auxReservoirPressure = Constants.MaxBrakePipePressure;
                            state.tripleValveMode = Components.PlainTripleValve.Mode.Charge;
                        }
                    }
                }
            });
        }
    }
}