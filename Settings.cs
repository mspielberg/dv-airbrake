using System.Collections.Generic;
using System.Linq;
using UnityModManagerNet;
using UnityEngine;

namespace DvMod.AirBrake
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        public List<string> selfLapCarIdentifiers = new List<string>() { "Loco Diesel" };

        // Reference: ~5 minutes to charge a main reservoir from empty
        // Default rate = 0.8 bar/s
        [Draw("Air compressor production")]
        public float compressorSpeed = Constants.MaxMainReservoirPressure / 0.8f / 300f;
        [Draw("Locomotive brake application speed")]
        public float locoApplySpeed = 10f;
        [Draw("Locomotive brake release speed")]
        public float locoReleaseSpeed = 10f;
        [Draw("Locomotive brake pipe recharge speed")]
        public float locoRechargeSpeed = 20f;

        [Draw("Train brake pipe balance speed", Min = 0, Max = 100)]
        public float pipeBalanceSpeed = 4;

        [Draw("Car brake application speed")]
        public float applySpeed = 2f;
        [Draw("Car brake release speed")]
        public float releaseSpeed = 10f;
        [Draw("Car reservoir recharge speed")]
        public float chargeSpeed = 4f;

        [Draw("Triple valve type")]
        public Components.TripleValveType tripleValveType = Components.TripleValveType.KType;
        [Draw("K-type triple pipe drain rate", VisibleOn = "tripleValveType|KType")]
        public float kTriplePipeDrainRate = 0.1f;
        [Draw("K-type triple retarded release rate", VisibleOn = "tripleValveType|KType")]
        public float kTripleRetardedReleaseRate = 0.5f;

        [Draw("Brake return spring strength")]
        public float returnSpringStrength = 0.5f;

        [Draw("Enable logging")] public bool enableLogging = false;
        public readonly string? version = Main.mod?.Info.Version;

        private void DrawSelfLapSettings()
        {
            GUILayout.BeginVertical();
            GUILayout.Label("Use self-lapping brake valves:");
            foreach (var kvp in Locomotives)
            {
                string identifier = kvp.Value;
                bool oldSelfLap = selfLapCarIdentifiers.Contains(identifier);
                bool newSelfLap = GUILayout.Toggle(oldSelfLap, identifier);
                if (oldSelfLap != newSelfLap)
                {
                    if (newSelfLap)
                        selfLapCarIdentifiers.Add(identifier);
                    else
                        selfLapCarIdentifiers.RemoveAll(x => x == identifier);
                }
            }
            GUILayout.EndVertical();
        }

        public void Draw()
        {
            DrawSelfLapSettings();
            this.Draw(Main.mod);
        }

        override public void Save(UnityModManager.ModEntry entry)
        {
            Save<Settings>(this, entry);
        }

        public void OnChange()
        {
        }

        private static IEnumerable<KeyValuePair<TrainCarType, string>> _Locomotives =
            Enumerable.Empty<KeyValuePair<TrainCarType, string>>();
        private static IEnumerable<KeyValuePair<TrainCarType, string>> Locomotives
        {
            get
            {
                if (_Locomotives.Any())
                    return _Locomotives;
                var vanillaLocomotives = new List<TrainCarType>()
                {
                    TrainCarType.LocoShunter,
                    TrainCarType.LocoSteamHeavy,
                    TrainCarType.LocoDiesel,
                };

                var cclMod = UnityModManager.FindMod("DVCustomCarLoader");
                if (cclMod?.Active ?? false)
                {
                    var carManagerType = cclMod.Assembly.GetType("DVCustomCarLoader.CustomCarManager");
                    if (carManagerType != null)
                    {
                        _Locomotives = ((IEnumerable<KeyValuePair<TrainCarType, string>>)carManagerType
                            .GetMethod("GetCustomCarList")
                            .Invoke(null, new object[0]))
                            .Where(p => CarTypes.IsLocomotive(p.Key))
                            .ToList();
                    }
                }

                _Locomotives = vanillaLocomotives
                    .Select(carType => new KeyValuePair<TrainCarType, string>(carType, carType.DisplayName()))
                    .Concat(_Locomotives);
                return _Locomotives;
            }
        }
    }
}
