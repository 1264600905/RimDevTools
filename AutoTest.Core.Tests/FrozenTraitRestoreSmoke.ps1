$ErrorActionPreference = 'Stop'
# Compile the actual restore method with minimal native-tracker contracts. This
# exercises both degrees and gene-removal side effects without loading a game.
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../source/AutoTestRunner/Game/Scenarios/ScenarioGameBackend.cs') -Raw
$start = $source.IndexOf('        private static void SetTrait(')
$end = $source.IndexOf('        private static void SetSkill(', $start)
if ($start -lt 0 -or $end -lt 0) { throw 'Actual SetTrait method not found' }
$method = $source.Substring($start, $end - $start)
$prefix = @'
using System;
using System.Linq;
using System.Collections.Generic;
public sealed class ScenarioConfigurationException : Exception { public ScenarioConfigurationException(string m) : base(m) { } }
public sealed class TraitData { public int degree; }
public sealed class TraitDef { public string name; public List<TraitData> degreeDatas = new(); }
public sealed class Trait {
    public TraitDef def; public int Degree; public bool GeneSource;
    public Trait(TraitDef def, int degree) { this.def = def; Degree = degree; }
}
public sealed class TraitSet {
    public List<Trait> allTraits = new(); public int GeneRemovals;
    public bool HasTrait(TraitDef def, int degree) => allTraits.Any(t => t.def == def && t.Degree == degree);
    public void RemoveTrait(Trait t) { allTraits.Remove(t); if (t.GeneSource) GeneRemovals++; }
    public void GainTrait(Trait t, bool suppressConflicts = false) {
        if (!suppressConflicts && allTraits.Any(x => x.def == t.def)) return;
        if (!HasTrait(t.def, t.Degree)) allTraits.Add(t);
    }
}
public sealed class Story { public TraitSet traits = new(); }
public sealed class Pawn { public Story story = new(); }
public static class ActualFrozenTraitRestore {
    public static TraitDef Definition;
    private static T Def<T>(string name) where T : class => Definition as T;
    private static void Need(object tracker, string name) { if (tracker == null) throw new Exception(name); }
    public static void Apply(Pawn pawn, int degree, bool restore) => SetTrait(pawn, "PsychicSensitivity", degree, restore);
'@
$suffix = @'
}
'@
Add-Type -TypeDefinition ($prefix + $method + $suffix) | Out-Null
$def = [TraitDef]::new()
$def.name = 'PsychicSensitivity'
foreach ($degree in @(-2, -1, 0)) { $data = [TraitData]::new(); $data.degree = $degree; $def.degreeDatas.Add($data) }
[ActualFrozenTraitRestore]::Definition = $def
$pawn = [Pawn]::new()
$inherited = [Trait]::new($def, -1)
$gene = [Trait]::new($def, -2); $gene.GeneSource = $true
$pawn.story.traits.allTraits.Add($inherited); $pawn.story.traits.allTraits.Add($gene)
foreach ($degree in @(-1, -2)) { [ActualFrozenTraitRestore]::Apply($pawn, $degree, $true) }
if ($pawn.story.traits.allTraits.Count -ne 2 -or $pawn.story.traits.GeneRemovals -ne 0) { throw 'Frozen restore deleted a degree or its source gene' }
Write-Output 'PASS: actual SetTrait preserves two frozen degrees and their source gene'
$missing = [Pawn]::new(); $missing.story.traits.allTraits.Add($gene)
[ActualFrozenTraitRestore]::Apply($missing, -1, $true)
if ($missing.story.traits.allTraits.Count -ne 2 -or $missing.story.traits.GeneRemovals -ne 0) { throw 'Adding missing frozen degree deleted native gene trait' }
Write-Output 'PASS: missing frozen degree is added alongside a native gene trait'
[ActualFrozenTraitRestore]::Apply($missing, -1, $true)
if ($missing.story.traits.allTraits.Count -ne 2) { throw 'Repeated restoration created duplicate traits' }
Write-Output 'PASS: repeated exact restore is idempotent'
$edited = [Pawn]::new(); $edited.story.traits.allTraits.Add([Trait]::new($def, -1))
[ActualFrozenTraitRestore]::Apply($edited, 0, $false)
if ($edited.story.traits.allTraits.Count -ne 1 -or $edited.story.traits.allTraits[0].Degree -ne 0) { throw 'Ordinary explicit trait editing no longer replaces the degree' }
Write-Output 'PASS: explicit trait edits retain normal replacement behavior'
