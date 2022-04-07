using UnityModManagerNet;

namespace DvMod.AirBrake
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Draw("Enable self-lapping on DE2")]
        public bool shunterSelfLap = false;
        [Draw("Enable self-lapping on SH282")]
        public bool steamHeavySelfLap = false;
        [Draw("Enable self-lapping on DE6")]
        public bool dieselSelfLap = true;

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
        public float chargeSpeed = 2f;

        [Draw("Triple valve type")]
        public Components.TripleValveType tripleValveType = Components.TripleValveType.KType;
        [Draw("K-type triple pipe drain rate", VisibleOn = "tripleValveType|KType")]
        public float kTriplePipeDrainRate = 0.1f;
        [Draw("K-type triple retarded release rate", VisibleOn = "tripleValveType|KType")]
        public float kTripleRetardedReleaseRate = 0.5f;

        [Draw("Brake return spring strength")]
        public float returnSpringStrength = 1f;

        [Draw("Enable logging")] public bool enableLogging = false;
        public readonly string? version = Main.mod?.Info.Version;

        override public void Save(UnityModManager.ModEntry entry)
        {
            Save<Settings>(this, entry);
        }

        public void OnChange()
        {
        }
    }
}
