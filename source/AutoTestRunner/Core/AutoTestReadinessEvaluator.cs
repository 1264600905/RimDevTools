using System;

namespace LogSyncMod.AutoTest
{
    public static class AutoTestReadinessEvaluator
    {
        public static AutoTestReadinessSnapshot Evaluate(AutoTestReadinessInput input)
        {
            input = input ?? new AutoTestReadinessInput();
            bool playing = string.Equals(input.ProgramState, "Playing", StringComparison.OrdinalIgnoreCase);
            AutoTestReadinessSnapshot result = new AutoTestReadinessSnapshot
            {
                HasCurrentGame = input.HasCurrentGame,
                MapCount = Math.Max(0, input.MapCount),
                HasCurrentMap = input.HasCurrentMap,
                ProgramState = input.ProgramState ?? string.Empty,
                LongEventPending = input.LongEventPending,
                TickManagerReady = input.TickManagerReady,
                WindowStackReady = input.WindowStackReady,
                SelectorReady = input.SelectorReady,
                ScreenFadeClear = input.ScreenFadeClear
            };
            result.GameDataReady = result.HasCurrentGame && playing && result.TickManagerReady;
            result.MapDataReady = result.MapCount > 0 && result.HasCurrentMap;
            result.Playable = result.GameDataReady && result.MapDataReady && result.WindowStackReady && result.SelectorReady;
            result.VisualReady = result.WindowStackReady && result.ScreenFadeClear && !result.LongEventPending;
            result.AutomationReady = result.Playable && result.VisualReady;
            result.Summary = "game=" + result.HasCurrentGame +
                " program=" + result.ProgramState +
                " maps=" + result.MapCount +
                " currentMap=" + result.HasCurrentMap +
                " longEvent=" + result.LongEventPending +
                " tickManager=" + result.TickManagerReady +
                " windows=" + result.WindowStackReady +
                " selector=" + result.SelectorReady +
                " fadeClear=" + result.ScreenFadeClear +
                " playable=" + result.Playable +
                " visual=" + result.VisualReady +
                " automation=" + result.AutomationReady;
            return result;
        }
    }
}
