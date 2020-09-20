using HarmonyLib;
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
            enabled = value;
            Updater.Enabled = value;
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
            public float locoApplySpeed = 3f;
            [Draw("Locomotive brake release speed")]
            public float locoReleaseSpeed = 2f;
            [Draw("Locomotive brake pipe recharge speed")]
            public float locoRechargeSpeed = 20f;

            [Draw("Train brake pipe balance speed", Min = 1, Max = 100)]
            public int pipeBalanceSpeed = 5;
            [Draw("Car brake application speed")]
            public float applySpeed = 3f;
            [Draw("Car brake release speed")]
            public float releaseSpeed = 0.1f;
            [Draw("Car reservoir recharge speed")]
            public float chargeSpeed = 0.5f;

            [Draw("Enable logging")] public bool enableLogging = false;

            override public void Save(UnityModManager.ModEntry entry)
            {
                Save<Settings>(this, entry);
            }

            public void OnChange()
            {
            }
        }
    }
}
