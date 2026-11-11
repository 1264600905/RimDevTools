$ErrorActionPreference = 'Stop'
# Run the actual combat-identity preparation method against isolated Def contracts.
# This reproduces the Colonist + nonhuman race override without launching RimWorld.
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../source/AutoTestRunner/Game/SquadGeneration.cs') -Raw
$start = $source.IndexOf('        private static XElement CombatConfiguration(')
$end = $source.IndexOf('        internal static ResolvedCombatRoster ResolveActor', $start)
if ($start -lt 0 -or $end -lt 0) { throw 'Actual CombatConfiguration method not found' }
$method = $source.Substring($start, $end - $start)
$prefix = @'
using System;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;
public sealed class ScenarioConfigurationException : Exception { public ScenarioConfigurationException(string m) : base(m) { } }
public sealed class RaceProperties { public bool Humanlike; }
public sealed class ThingDef { public string defName; public RaceProperties race = new(); }
public sealed class PawnKindDef { public string defName; public ThingDef race; public RaceProperties RaceProps => race.race; }
public sealed class XenotypeDef { public string defName; public bool canGenerateAsCombatant = true; }
public sealed class Faction { }
public static class ModsConfig { public static bool BiotechActive = true; }
public static class DefDatabase<T> { public static Dictionary<string,T> Values = new(); public static T GetNamed(string name) => Values[name]; }
public static class PawnGenerator {
    public static int Calls;
    public static List<KeyValuePair<XenotypeDef,float>> XenotypesAvailableFor(PawnKindDef k, Faction faction = null) {
        Calls++; return new() { new(new XenotypeDef { defName="Baseliner" }, 1) };
    }
}
public static class NativePickStub {
    public static bool TryRandomElementByWeight<T>(this List<T> list, Func<T,float> weight, out T result) {
        result = list.Count > 0 ? list[0] : default(T); return list.Count > 0;
    }
}
public static class ActualCombatIdentity {
    public static PawnKindDef NativeKind;
    private static PawnKindDef Kind(string name) => NativeKind;
    private static XenotypeDef Xeno(string name) => new() { defName=name };
    public static XElement Apply(XElement actor) => CombatConfiguration(actor, new Faction());
'@
Add-Type -TypeDefinition ($prefix + $method + "`n}") | Out-Null
$human = [ThingDef]::new(); $human.defName = 'Human'; $human.race.Humanlike = $true
$nonhuman = [ThingDef]::new(); $nonhuman.defName = 'Trispike'; $nonhuman.race.Humanlike = $false
[DefDatabase[ThingDef]]::Values.Add('Human', $human)
[DefDatabase[ThingDef]]::Values.Add('Trispike', $nonhuman)
$kind = [PawnKindDef]::new(); $kind.defName = 'Colonist'; $kind.race = $human
[ActualCombatIdentity]::NativeKind = $kind
$result = [ActualCombatIdentity]::Apply([System.Xml.Linq.XElement]::Parse('<PawnTemplate><basePawnKind>Colonist</basePawnKind><race>Trispike</race></PawnTemplate>'))
if ($null -ne $result.Element('Xenotype') -or [PawnGenerator]::Calls -ne 0) { throw 'Nonhuman override received a human xenotype' }
Write-Output 'PASS: actual Colonist + Trispike preparation never auto-adds a xenotype or requests its gene tracker'
$result = [ActualCombatIdentity]::Apply([System.Xml.Linq.XElement]::Parse('<PawnTemplate><basePawnKind>Colonist</basePawnKind></PawnTemplate>'))
if ($null -eq $result.Element('Xenotype') -or [PawnGenerator]::Calls -ne 1) { throw 'Human native xenotype selection was lost' }
Write-Output 'PASS: human units retain native combat xenotype selection'
$rejected = $false
try { [ActualCombatIdentity]::Apply([System.Xml.Linq.XElement]::Parse('<PawnTemplate><basePawnKind>Colonist</basePawnKind><race>Trispike</race><Xenotype def="Baseliner"/></PawnTemplate>')) | Out-Null }
catch { $rejected = $true }
if (-not $rejected) { throw 'Explicit incompatible xenotype was accepted' }
Write-Output 'PASS: explicit human xenotype on a nonhuman final race is rejected before pawn generation'
