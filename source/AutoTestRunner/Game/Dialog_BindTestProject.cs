using System;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed class Dialog_BindTestProject : Window
    {
        private readonly Action<ManualProjectContext> completed;
        private string root = "";
        private string error;
        internal Dialog_BindTestProject(Action<ManualProjectContext> completed, string diagnostic = null)
        {
            this.completed = completed; error = diagnostic;
            forcePause = true; absorbInputAroundWindow = true; doCloseX = true; closeOnAccept = false;
        }
        public override Vector2 InitialSize => new Vector2(800f, 260f);
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0, 0, rect.width, 34), "绑定 RimDevTools 测试项目（项目根目录，包含 mod_config.jsonc）");
            root = Widgets.TextField(new Rect(0, 42, rect.width, 32), root);
            if (!string.IsNullOrWhiteSpace(error)) Widgets.Label(new Rect(0, 84, rect.width, 76), error);
            if (Widgets.ButtonText(new Rect(rect.width - 170, rect.height - 40, 170, 36), "绑定并继续"))
            {
                try { var context = ManualProjectBinding.Bind(root.Trim().Trim('"')); completed(context); Close(); }
                catch (Exception ex) { error = ex.Message; }
            }
        }
    }
}
