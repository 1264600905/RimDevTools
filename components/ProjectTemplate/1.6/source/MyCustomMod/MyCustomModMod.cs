using UnityEngine;
using Verse;

namespace MyCustomMod
{
    public class MyCustomModMod : Mod
    {
        public static MyCustomModSettings settings;

        public MyCustomModMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<MyCustomModSettings>();
        }

        public override string SettingsCategory()
        {
            return "MyCustomMod";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            float y = 60f;
            const float rowHeight = 32f;
            Widgets.CheckboxLabeled(new Rect(0, y, inRect.width, rowHeight), "ShowDebugLog".Translate(), ref settings.enableLog);
            settings.Write();
        }
    }
}
