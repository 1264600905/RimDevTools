using System;
using UnityEngine;
using Verse;

namespace LogSyncMod
{
    public static class TestAudioController
    {
        private static readonly object Sync = new object();
        private static bool armed;
        private static float previousPrefsVolumeMaster;
        private static float previousListenerVolume;
        private static int correctionCount;
        private static bool arenaAudio;

        private static bool IsArenaSession => LogSyncMod.settings?.testSessionActive == true && LogSyncMod.settings.testSessionKind == "arena";
        public static object Status => new { armed, arenaAudio, master = Prefs.VolumeMaster, game = Prefs.VolumeGame, music = Prefs.VolumeMusic, ambient = Prefs.VolumeAmbient, ui = Prefs.VolumeUI, listener = AudioListener.volume, correctionCount };

        private static void ApplyArenaVolume()
        {
            Prefs.VolumeMaster = 1f;
            AudioListener.volume = 1f;
        }

        public static void ArmIfRequested()
        {
            lock (Sync)
            {
                if (armed || LogSyncMod.settings == null || !LogSyncMod.settings.testMuteAudio && !IsArenaSession)
                {
                    return;
                }

                try
                {
                    previousPrefsVolumeMaster = Prefs.VolumeMaster;
                    previousListenerVolume = AudioListener.volume;
                    arenaAudio = IsArenaSession;
                    if (arenaAudio)
                    {
                        ApplyArenaVolume();
                    }
                    else
                    {
                        Prefs.VolumeMaster = 0f;
                        AudioListener.volume = 0f;
                    }
                    correctionCount = 0;
                    armed = true;
                    Log.Message(
                        (arenaAudio ? "[LogSyncMod] Arena master volume set to 100%. reason=arm" : "[LogSyncMod] Test audio muted. reason=arm") +
                        " previousPrefsVolumeMaster=" + previousPrefsVolumeMaster.ToString("R") +
                        " previousAudioListenerVolume=" + previousListenerVolume.ToString("R") +
                        " currentPrefsVolumeMaster=" + Prefs.VolumeMaster.ToString("R") +
                        " currentAudioListenerVolume=" + AudioListener.volume.ToString("R"));
                }
                catch (Exception ex)
                {
                    armed = false;
                    Log.Warning("[LogSyncMod] Failed to apply test audio settings: " + ex.Message);
                }
            }
        }

        public static void Maintain(string reason)
        {
            lock (Sync)
            {
                if (!armed)
                {
                    return;
                }

                try
                {
                    if (arenaAudio)
                    {
                        if (Prefs.VolumeMaster == 1f && AudioListener.volume == 1f) return;
                        ApplyArenaVolume();
                        correctionCount++;
                        Log.Message("[LogSyncMod] Arena master volume restored to 100%. reason=" + (reason ?? "unknown") + " correctionCount=" + correctionCount);
                        return;
                    }
                    float prefsVolume = Prefs.VolumeMaster;
                    float listenerVolume = AudioListener.volume;
                    if (Math.Abs(prefsVolume) <= 0.0001f && Math.Abs(listenerVolume) <= 0.0001f)
                    {
                        return;
                    }

                    Prefs.VolumeMaster = 0f;
                    AudioListener.volume = 0f;
                    correctionCount++;
                    Log.Message(
                        "[LogSyncMod] Test audio mute re-applied. reason=" + (reason ?? "unknown") +
                        " observedPrefsVolumeMaster=" + prefsVolume.ToString("R") +
                        " observedAudioListenerVolume=" + listenerVolume.ToString("R") +
                        " correctionCount=" + correctionCount);
                }
                catch (Exception ex)
                {
                    Log.Warning("[LogSyncMod] Failed to maintain test audio settings. reason=" + (reason ?? "unknown") + " error=" + ex.Message);
                }
            }
        }

        public static void Restore(string reason = "restore")
        {
            lock (Sync)
            {
                if (!armed)
                {
                    return;
                }

                try
                {
                    Prefs.VolumeMaster = previousPrefsVolumeMaster;
                    AudioListener.volume = previousListenerVolume;
                    Log.Message(
                        "[LogSyncMod] Test audio restored. reason=" + (reason ?? "unknown") +
                        " restoredPrefsVolumeMaster=" + previousPrefsVolumeMaster.ToString("R") +
                        " restoredAudioListenerVolume=" + previousListenerVolume.ToString("R") +
                        " currentPrefsVolumeMaster=" + Prefs.VolumeMaster.ToString("R") +
                        " currentAudioListenerVolume=" + AudioListener.volume.ToString("R") +
                        " correctionCount=" + correctionCount);
                }
                catch (Exception ex)
                {
                    Log.Warning("[LogSyncMod] Failed to restore test audio: " + ex.Message);
                }
                finally
                {
                    armed = false;
                    correctionCount = 0;
                }
            }
        }
    }
}
