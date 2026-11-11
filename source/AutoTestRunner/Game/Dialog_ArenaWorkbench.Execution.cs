using System;
using System.Linq;
using LogSyncMod.AutoTest.Scenarios;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class Dialog_ArenaWorkbench
    {
        private void ValidateCurrentEvent()
        {
            if (document.Schedule.Format == "groupcombat")
            {
                ArenaWorkbench.CompileGroup(document);
                var arena = ArenaWorkbench.Copy(document); arena.Schedule.Format = "knockout";
                ArenaWorkbenchDefinitions.Validate(ArenaWorkbench.Compile(arena));
            }
            else ArenaWorkbenchDefinitions.Validate(ArenaWorkbench.Compile(document));
        }
        private void Start()
        {
            Protect(() => {
                NormalizeApparel(); ValidateCurrentEvent();
                var group = document.Schedule.Format == "groupcombat" ? ArenaWorkbench.CompileGroup(document, true) : null;
                var arena = group == null ? ArenaWorkbench.Compile(document) : null;
                void Begin(ManualProjectContext project)
                {
                    Protect(() => { ArenaWorkbenchStorage.Save(document, false); DisposePreview();
                        if (group != null) GroupCombatService.BeginManual(group, project); else ArenaService.BeginManual(arena, project);
                        Close();
                    });
                }
                ManualProjectContext context;
                try { context = ManualProjectBinding.Resolve(); }
                catch (Exception ex) { Find.WindowStack.Add(new Dialog_BindTestProject(Begin, ex.Message)); return; }
                Begin(context);
            });
        }
    }
}
