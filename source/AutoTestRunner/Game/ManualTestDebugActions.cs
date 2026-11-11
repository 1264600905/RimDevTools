using System;
using System.Collections.Generic;
using System.Linq;
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

        [DebugAction("LogSyncMod", "群战工作台", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void OpenArenaWorkbench()
        {
            try { Find.WindowStack.Add(new Dialog_ArenaWorkbench(ArenaWorkbenchStorage.Load())); }
            catch (Exception ex) { Messages.Message("群战工作台打开失败：" + ex.Message, MessageTypeDefOf.RejectInput, false); }
        }

        [DebugAction("LogSyncMod", "自动测试", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void OpenTests()
        {
            void Open(ManualProjectContext project) { Catalog.RefreshInstances(ManualTestService.SourceMap ?? Find.CurrentMap); Find.WindowStack.Add(new Dialog_ManualAutoTests(Catalog, project)); }
            ManualProjectContext context;
            try { context = ManualProjectBinding.Resolve(); }
            catch (Exception ex) { Find.WindowStack.Add(new Dialog_BindTestProject(Open, ex.Message)); return; }
            try { Open(context); }
            catch (Exception ex) { Messages.Message("自动测试窗口打开失败：" + ex.Message, MessageTypeDefOf.RejectInput, false); }
        }
        [DebugAction("LogSyncMod", "导出角色模板（框选）", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void CaptureSelectedPawn()
        {
            try { EnsureExportAllowed(); }
            catch (Exception ex) { Messages.Message(ex.Message, MessageTypeDefOf.RejectInput, false); return; }
            void Select(ManualProjectContext project)
            {
                var map = Find.CurrentMap;
                Messages.Message("在地图上点击矩形的两个角，只导出框内的存活角色；右键或 Esc 取消。", MessageTypeDefOf.NeutralEvent, false);
                DebugToolsGeneral.GenericRectTool("导出角色模板", rect =>
                {
                    try
                    {
                        if (!Prefs.DevMode || Find.CurrentMap != map) throw new InvalidOperationException("地图已切换，请重新框选角色。");
                        var result = ExportRectangle(map, rect, project);
                        Messages.Message("已导出 " + result.Paths.Count + " 个角色模板" + (result.Errors.Count == 0 ? "" : "，失败 " + result.Errors.Count + " 个")
                            + "。\n位置：" + System.IO.Path.GetDirectoryName(result.Paths.FirstOrDefault() ?? TemporaryPawnData.PathFor(project.Root))
                            + "\n可在自动测试窗口的“选择模板”中使用；当前测试角色未更换。",
                            result.Errors.Count == 0 ? MessageTypeDefOf.TaskCompletion : MessageTypeDefOf.RejectInput, false);
                        foreach (var error in result.Errors) Log.Warning("[LogSyncMod] Pawn template export: " + error);
                    }
                    catch (Exception ex) { Messages.Message("导出角色模板失败：" + ex.Message, MessageTypeDefOf.RejectInput, false); }
                }, closeOnComplete: true);
            }
            ManualProjectContext context;
            try { context = ManualProjectBinding.Resolve(); }
            catch (Exception ex) { Find.WindowStack.Add(new Dialog_BindTestProject(Select, ex.Message)); return; }
            Select(context);
        }

        private static void EnsureExportAllowed()
        {
            if (ManualTestService.Busy || ArenaService.Busy || GroupCombatService.Busy || CombatFightService.Busy || global::LogSyncMod.LogSyncMod.settings is { testSessionActive: true } settings && settings.testSessionKind != "manual")
                throw new InvalidOperationException("已有测试会话正在运行，请在结束后导出角色。");
        }

        internal static List<Pawn> PawnsInRectangle(Map map, CellRect rect) => map == null ? new List<Pawn>() : map.mapPawns.AllPawnsSpawned
            .Where(p => p.Spawned && p.Map == map && !p.Dead && !p.Destroyed && p.kindDef != null && rect.Contains(p.Position))
            .OrderBy(p => p.thingIDNumber).ToList();

        internal static PawnTemplateExportResult ExportRectangle(Map map, CellRect rect, ManualProjectContext project)
        {
            EnsureExportAllowed();
            if (map == null || map != Find.CurrentMap) throw new InvalidOperationException("请在当前地图框选角色。");
            var pawns = PawnsInRectangle(map, rect);
            if (pawns.Count == 0) throw new InvalidOperationException("框选区域内没有可导出的存活角色，未导出任何模板。");
            var result = new PawnTemplateExportResult();
            foreach (var pawn in pawns)
            {
                try { result.Paths.Add(TemporaryPawnData.Export(project.Root, TemporaryPawnCapture.Capture(pawn), pawn.LabelShort)); }
                catch (Exception ex) { result.Errors.Add(pawn.LabelShort + "：" + ex.Message); }
            }
            return result;
        }
        internal static string CaptureForProject(Pawn pawn, ManualProjectContext project)
        {
            if (ManualTestService.Busy) throw new InvalidOperationException("A manual test invocation is active");
            return TemporaryPawnCapture.CaptureAndSave(pawn, project.Root);
        }
    }

    internal sealed class PawnTemplateExportResult
    {
        internal readonly List<string> Paths = new List<string>();
        internal readonly List<string> Errors = new List<string>();
    }
}
