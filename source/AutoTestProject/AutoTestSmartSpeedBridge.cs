using System;
using System.Linq;
using System.Reflection;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed class AutoTestSmartSpeedBridge
    {
        private const string PackageId = "sarg.smartspeed";
        private const string AssemblyName = "SmartSpeed";
        private const string SettingsTypeName = "SmartSpeed.SmartSpeed_Settings";
        private const string OptionTypeName = "SmartSpeed.Option";
        private const string FastSpeedFieldName = "fastSpeed";
        private const string CurrentSettingFieldName = "currSetting";
        private const string IgnoreOptionName = "Ignore";
        private const float AutoTestFastSpeedMultiplier = 2f;

        private bool armed;
        private float previousFastSpeed;
        private object previousCurrentSetting;
        private TimeSpeed previousTimeSpeed;
        private FieldInfo fastSpeedField;
        private FieldInfo currentSettingField;
        private Type optionType;
        private bool pauseBypassActive;

        public bool IsArmed => armed;

        public bool TryArm(out string details)
        {
            details = string.Empty;
            if (armed)
            {
                details = "SmartSpeedAlreadyArmed";
                return true;
            }

            if (!TryResolveSmartSpeed(out string resolveDetails))
            {
                details = resolveDetails;
                return false;
            }

            TickManager tickManager = Find.TickManager;
            if (tickManager == null)
            {
                details = "SmartSpeedResolvedButTickManagerMissing";
                return false;
            }

            previousFastSpeed = Convert.ToSingle(fastSpeedField.GetValue(null));
            previousCurrentSetting = currentSettingField.GetValue(null);
            previousTimeSpeed = tickManager.CurTimeSpeed;

            fastSpeedField.SetValue(null, AutoTestFastSpeedMultiplier);
            currentSettingField.SetValue(null, Enum.Parse(optionType, IgnoreOptionName));
            tickManager.CurTimeSpeed = TimeSpeed.Fast;
            armed = true;
            details = "PackageId=" + PackageId +
                " fastSpeed=" + previousFastSpeed + "->" + AutoTestFastSpeedMultiplier +
                " currSetting=" + previousCurrentSetting + "->" + IgnoreOptionName +
                " curTimeSpeed=" + previousTimeSpeed + "->Fast";
            return true;
        }

        public bool TryMaintain(out string details)
        {
            details = string.Empty;
            if (!armed)
            {
                details = "SmartSpeedNotArmed";
                return false;
            }

            TickManager tickManager = Find.TickManager;
            if (tickManager == null)
            {
                details = "TickManagerMissing";
                return false;
            }

            bool adjusted = false;
            if (pauseBypassActive)
            {
                details = "PauseBypassActive curTimeSpeed=" + tickManager.CurTimeSpeed +
                    " paused=" + tickManager.Paused +
                    " forcePaused=" + tickManager.ForcePaused +
                    " fastSpeed=" + Convert.ToSingle(fastSpeedField.GetValue(null)) +
                    " currSetting=" + currentSettingField.GetValue(null);
                return false;
            }

            if (Convert.ToSingle(fastSpeedField.GetValue(null)) != AutoTestFastSpeedMultiplier)
            {
                fastSpeedField.SetValue(null, AutoTestFastSpeedMultiplier);
                adjusted = true;
            }

            object currentSetting = currentSettingField.GetValue(null);
            if (!string.Equals(currentSetting?.ToString(), IgnoreOptionName, StringComparison.Ordinal))
            {
                currentSettingField.SetValue(null, Enum.Parse(optionType, IgnoreOptionName));
                adjusted = true;
            }

            if (tickManager.CurTimeSpeed != TimeSpeed.Fast)
            {
                tickManager.CurTimeSpeed = TimeSpeed.Fast;
                adjusted = true;
            }

            details = "curTimeSpeed=" + tickManager.CurTimeSpeed +
                " paused=" + tickManager.Paused +
                " forcePaused=" + tickManager.ForcePaused +
                " fastSpeed=" + Convert.ToSingle(fastSpeedField.GetValue(null)) +
                " currSetting=" + currentSettingField.GetValue(null);
            return adjusted;
        }

        public void SetPauseBypass(bool active)
        {
            pauseBypassActive = active;
        }

        public void Restore(out string details)
        {
            details = string.Empty;
            if (!armed)
            {
                details = "SmartSpeedNotArmed";
                return;
            }

            TickManager tickManager = Find.TickManager;
            if (fastSpeedField != null)
            {
                fastSpeedField.SetValue(null, previousFastSpeed);
            }

            if (currentSettingField != null)
            {
                currentSettingField.SetValue(null, previousCurrentSetting);
            }

            if (tickManager != null)
            {
                tickManager.CurTimeSpeed = previousTimeSpeed;
            }

            details = "fastSpeedRestored=" + previousFastSpeed +
                " currSettingRestored=" + previousCurrentSetting +
                " curTimeSpeedRestored=" + previousTimeSpeed;
            armed = false;
            pauseBypassActive = false;
            fastSpeedField = null;
            currentSettingField = null;
            optionType = null;
            previousCurrentSetting = null;
        }

        private bool TryResolveSmartSpeed(out string details)
        {
            details = string.Empty;
            if (!LoadedModManager.RunningMods.Any(mod =>
                string.Equals(mod.PackageIdPlayerFacing, PackageId, StringComparison.OrdinalIgnoreCase)))
            {
                details = "SmartSpeedModNotActive";
                return false;
            }

            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, AssemblyName, StringComparison.OrdinalIgnoreCase));
            if (assembly == null)
            {
                details = "SmartSpeedAssemblyNotLoaded";
                return false;
            }

            Type settingsType = assembly.GetType(SettingsTypeName, throwOnError: false);
            optionType = assembly.GetType(OptionTypeName, throwOnError: false);
            if (settingsType == null || optionType == null)
            {
                details = "SmartSpeedTypesMissing";
                return false;
            }

            fastSpeedField = settingsType.GetField(FastSpeedFieldName, BindingFlags.Public | BindingFlags.Static);
            currentSettingField = settingsType.GetField(CurrentSettingFieldName, BindingFlags.Public | BindingFlags.Static);
            if (fastSpeedField == null || currentSettingField == null)
            {
                details = "SmartSpeedFieldsMissing";
                return false;
            }

            details = "SmartSpeedResolved";
            return true;
        }
    }
}
