using UnityEngine;
using Verse;

namespace TempProject
{
    public class TempProjectMod : Mod
    {
        public static TempProjectSettings settings;

        public TempProjectMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<TempProjectSettings>();
        }

        public override string SettingsCategory()
        {
            return "TempProject";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            float y = 60f;
            float rowHeight = 32f;
            float gap = 12f;

            Widgets.CheckboxLabeled(new Rect(0, y, inRect.width, rowHeight), "ModEnabled".Translate(), ref settings.modEnabled);
            y += rowHeight + gap;

            Widgets.CheckboxLabeled(new Rect(0, y, inRect.width, rowHeight), "EnableDeveloperTools".Translate(), ref settings.enableDeveloperTools);
            y += rowHeight + gap;

            if (settings.enableDeveloperTools)
            {
                Widgets.CheckboxLabeled(new Rect(20, y, inRect.width - 20, rowHeight), "ShowDebugLog".Translate(), ref settings.enableLog);
                y += rowHeight + gap;

                Widgets.CheckboxLabeled(new Rect(20, y, inRect.width - 20, rowHeight), "ForcePlaceBlueprint".Translate(), ref settings.forcePlaceBlueprint);
                y += rowHeight + gap;
            }
            else
            {
                if (settings.enableLog || settings.forcePlaceBlueprint)
                {
                    settings.enableLog = false;
                    settings.forcePlaceBlueprint = false;
                    settings.Write();
                }

                Color old = GUI.color;
                GUI.color = Color.gray;
                Widgets.CheckboxLabeled(new Rect(20, y, inRect.width - 20, rowHeight), "ShowDebugLog".Translate(), ref settings.enableLog);
                y += rowHeight + gap;
                Widgets.CheckboxLabeled(new Rect(20, y, inRect.width - 20, rowHeight), "ForcePlaceBlueprint".Translate(), ref settings.forcePlaceBlueprint);
                y += rowHeight + gap;
                GUI.color = old;
            }

            float buttonWidth = 140f;
            float buttonHeight = 32f;
            Rect buttonRect = new Rect(0, y, buttonWidth, buttonHeight);
            if (Widgets.ButtonText(buttonRect, "ResetToDefault".Translate()))
            {
                settings.Reset();
                settings.Write();
            }

            settings.Write();
        }
    }
}
