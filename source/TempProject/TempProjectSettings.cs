using System.Collections.Generic;
using Verse;

namespace TempProject
{
    /// <summary>
    /// TempProjectSettings
    /// 通用 Mod 设置数据类：启用开关、开发者工具、调试日志与强制放置蓝图示例项。
    /// </summary>
    public class TempProjectSettings : ModSettings
    {
        public bool modEnabled = true;
        public bool enableDeveloperTools = false;
        public bool enableLog = false;
        public bool forcePlaceBlueprint = false;

        // 可选：收藏与简单标志示例（保留为通用占位，避免强耦合）
        public HashSet<string> favoriteItems = new HashSet<string>();
        private bool featureToggleA = true;

        public void Reset()
        {
            modEnabled = true;
            enableDeveloperTools = false;
            enableLog = false;
            forcePlaceBlueprint = false;
            featureToggleA = true;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref modEnabled, nameof(modEnabled), true);
            Scribe_Values.Look(ref enableDeveloperTools, nameof(enableDeveloperTools), false);
            Scribe_Values.Look(ref enableLog, nameof(enableLog), false);
            Scribe_Values.Look(ref forcePlaceBlueprint, nameof(forcePlaceBlueprint), false);
            Scribe_Values.Look(ref featureToggleA, nameof(featureToggleA), true);
            Scribe_Collections.Look(ref favoriteItems, nameof(favoriteItems), LookMode.Value);
            if (favoriteItems == null) favoriteItems = new HashSet<string>();
        }

        public void SetFeatureToggleA(bool value)
        {
            featureToggleA = value;
            Write();
        }

        public bool GetFeatureToggleA => featureToggleA;
    }
}


