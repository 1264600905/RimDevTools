using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace LogSyncMod.AutoTest
{
    // Native GenerateMap owns the Rand push/pop. Only its initial seed is replaced for one test map.
    internal sealed class ScenarioMapSeed : IDisposable
    {
        [ThreadStatic] private static ScenarioMapSeed current;
        private readonly ScenarioMapSeed previous;
        private readonly int seed;
        private bool applied;
        internal static bool Supported;
        internal ScenarioMapSeed(int seed) { previous = current; current = this; this.seed = seed; }
        internal static int Resolve(int native)
        { if (current == null || current.applied) return native; current.applied = true; return current.seed; }
        public void Dispose() { current = previous; }
        internal void Verify() { if (!applied) throw new InvalidOperationException("Native map generator did not accept the requested scenario seed"); }
    }

    [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateMap))]
    internal static class ScenarioMapSeedPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var setter = AccessTools.PropertySetter(typeof(Rand), nameof(Rand.Seed));
            ScenarioMapSeed.Supported = code.Count(i => i.Calls(setter)) == 1;
            if (!ScenarioMapSeed.Supported) return code;
            var result = new List<CodeInstruction>();
            foreach (var instruction in code)
            {
                if (instruction.Calls(setter))
                {
                    var replace = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ScenarioMapSeed), nameof(ScenarioMapSeed.Resolve)));
                    replace.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    result.Add(replace);
                }
                result.Add(instruction);
            }
            return result;
        }
    }
}
