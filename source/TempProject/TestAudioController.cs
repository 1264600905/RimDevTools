using System;
using UnityEngine;
using Verse;

namespace LogSyncMod
{
    public static class TestAudioController
    {
        private static readonly object Sync = new object();
        private static bool armed;
        private static float previousVolume;

        public static void ArmIfRequested()
        {
            lock (Sync)
            {
                if (armed || LogSyncMod.settings == null || !LogSyncMod.settings.testMuteAudio)
                {
                    return;
                }

                try
                {
                    previousVolume = AudioListener.volume;
                    AudioListener.volume = 0f;
                    armed = true;
                    Log.Message("[LogSyncMod] Test audio muted. previousVolume=" + previousVolume.ToString("R"));
                }
                catch (Exception ex)
                {
                    armed = false;
                    Log.Warning("[LogSyncMod] Failed to mute test audio: " + ex.Message);
                }
            }
        }

        public static void Restore()
        {
            lock (Sync)
            {
                if (!armed)
                {
                    return;
                }

                try
                {
                    AudioListener.volume = previousVolume;
                    Log.Message("[LogSyncMod] Test audio restored. volume=" + previousVolume.ToString("R"));
                }
                catch (Exception ex)
                {
                    Log.Warning("[LogSyncMod] Failed to restore test audio: " + ex.Message);
                }
                finally
                {
                    armed = false;
                }
            }
        }
    }
}
