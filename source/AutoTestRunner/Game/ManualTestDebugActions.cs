using System;
using LudeonTK;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class ManualTestDebugActions
    {
        private static ManualTestCatalog catalog;
        internal static ManualTestCatalog Catalog => catalog ??= new ManualTestCatalog(Find.CurrentMap);

        [DebugAction("RimDevTools", "自动测试", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void OpenTests()
        {
            void Open(ManualProjectContext project) { Catalog.RefreshInstances(ManualTestService.SourceMap ?? Find.CurrentMap); Find.WindowStack.Add(new Dialog_ManualAutoTests(Catalog, project)); }
            ManualProjectContext context;
            try { context = ManualProjectBinding.Resolve(); }
            catch (Exception ex) { Find.WindowStack.Add(new Dialog_BindTestProject(Open, ex.Message)); return; }
            try { Open(context); }
            catch (Exception ex) { Messages.Message("自动测试窗口打开失败：" + ex.Message, MessageTypeDefOf.RejectInput, false); }
        }
        [DebugAction("RimDevTools", "暂存测试角色", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void CaptureSelectedPawn()
        {
            if (ManualTestService.Busy || global::LogSyncMod.LogSyncMod.settings is { testSessionActive: true } settings && settings.testSessionKind != "manual") { Messages.Message("已有测试会话正在运行，请在结束后暂存角色。", MessageTypeDefOf.RejectInput, false); return; }
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (pawn == null) { Messages.Message("请先选中一名要暂存的角色。", MessageTypeDefOf.RejectInput, false); return; }
            void Capture(ManualProjectContext project)
            {
                var path = CaptureForProject(pawn, project);
                Messages.Message("已暂存测试角色 XML：" + path, MessageTypeDefOf.TaskCompletion, false);
            }
            ManualProjectContext context;
            try { context = ManualProjectBinding.Resolve(); }
            catch (Exception ex) { Find.WindowStack.Add(new Dialog_BindTestProject(Capture, ex.Message)); return; }
            try { Capture(context); }
            catch (Exception ex) { Messages.Message("暂存角色失败：" + ex.Message, MessageTypeDefOf.RejectInput, false); }
        }
        internal static string CaptureForProject(Pawn pawn, ManualProjectContext project)
        {
            if (ManualTestService.Busy) throw new InvalidOperationException("A manual test invocation is active");
            return TemporaryPawnCapture.CaptureAndSave(pawn, project.Root);
        }
    }
}
