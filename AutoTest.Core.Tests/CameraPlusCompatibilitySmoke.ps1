param(
    [Parameter(Mandatory = $true)][string]$HarmonyDllPath,
    [ValidateSet('All', 'Absent', 'NoTargets', 'Compatible', 'ChangedField')][string]$Mode = 'All'
)

# Run the real compatibility patch against the installed Harmony in isolated, headless processes.
# No game launch, deployment, settings, log files or evidence directories are used.
$ErrorActionPreference = 'Stop'
if ($Mode -eq 'All') {
    foreach ($case in @('Absent', 'NoTargets', 'Compatible', 'ChangedField')) {
        & (Get-Process -Id $PID).Path -NoProfile -File $PSCommandPath -HarmonyDllPath $HarmonyDllPath -Mode $case
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    exit 0
}

$harmonyPath = (Resolve-Path -LiteralPath $HarmonyDllPath).Path
[Reflection.Assembly]::LoadFrom($harmonyPath) | Out-Null
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../source/AutoTestRunner/Game/CameraPlusCombatCompatibility.cs') -Raw
$stubs = @'
namespace Verse {
    public sealed class Pawn { }
    public static class Find { public static object CurrentMap; }
    public static class Log {
        public static readonly System.Collections.Generic.List<string> Warnings = new();
        public static void Warning(string message) { Warnings.Add(message); }
    }
}
namespace LogSyncMod.AutoTest {
    public static class GroupCombatCamera { public static bool Active; }
    public static class CombatFightService { public static object Map; }
}
'@
if ($Mode -eq 'NoTargets') {
    $stubs += "`nnamespace CameraPlus { public sealed class MarkerDecision { public bool edgeEnabled; } }"
}
elseif ($Mode -in @('Compatible', 'ChangedField')) {
    $number = if ($Mode -eq 'ChangedField') { 'float' } else { 'int' }
    $stubs += @"
namespace CameraPlus {
    public sealed class CameraPlusSettings { public $number dotSize = 10; public bool edgeIndicators = true; }
    public sealed class DotConfig { public int showBelowPixels = 20; public bool useEdge = true; }
    public sealed class MarkerDecision {
        public bool edgeEnabled;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static $number For(CameraPlusSettings settings, DotConfig config, Verse.Pawn pawn)
            => settings.dotSize + config.showBelowPixels + (settings.edgeIndicators ? 1 : 0) + (config.useEdge ? 1 : 0);
    }
}
"@
}
$references = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName) + $harmonyPath
$types = Add-Type -TypeDefinition ($source + "`n" + $stubs) -ReferencedAssemblies $references -CompilerOptions '/nowarn:1701' -PassThru
$patch = $types | Where-Object FullName -eq 'LogSyncMod.AutoTest.CameraPlusCombatCompatibility'
$harmony = [HarmonyLib.Harmony]::new('logsync.compatibility.headless.' + $Mode)
try {
    $harmony.CreateClassProcessor($patch).Patch() | Out-Null
    if ($Mode -eq 'Compatible') {
        if ([Verse.Log]::Warnings.Count -ne 0) { throw 'Compatible installation produced an unexpected warning' }
        $settings = [CameraPlus.CameraPlusSettings]::new(); $config = [CameraPlus.DotConfig]::new(); $pawn = [Verse.Pawn]::new()
        $method = [CameraPlus.MarkerDecision].GetMethod('For')
        $map = [object]::new(); [Verse.Find]::CurrentMap = $map; [LogSyncMod.AutoTest.CombatFightService]::Map = $map
        if ($method.Invoke($null, @($settings, $config, $pawn)) -ne 32) { throw 'Ordinary camera behavior changed' }
        [LogSyncMod.AutoTest.GroupCombatCamera]::Active = $true
        if ($method.Invoke($null, @($settings, $config, $pawn)) -ne -4) { throw 'Dedicated battle camera compatibility did not apply' }
        [Verse.Find]::CurrentMap = [object]::new()
        if ($method.Invoke($null, @($settings, $config, $pawn)) -ne 32) { throw 'Compatibility leaked outside the battle map' }
        if ($settings.dotSize -ne 10 -or -not $settings.edgeIndicators -or $config.showBelowPixels -ne 20 -or -not $config.useEdge) { throw 'Camera settings were mutated' }
    }
    else {
        if ([Verse.Log]::Warnings.Count -ne 1) { throw "Expected one nonfatal warning, received $([Verse.Log]::Warnings.Count)" }
        $harmony.CreateClassProcessor($patch).Patch() | Out-Null
        if ([Verse.Log]::Warnings.Count -ne 1) { throw 'Skipped compatibility repeatedly logs warnings' }
        if ($Mode -eq 'ChangedField') {
            $settings = [CameraPlus.CameraPlusSettings]::new(); $config = [CameraPlus.DotConfig]::new(); $pawn = [Verse.Pawn]::new()
            [LogSyncMod.AutoTest.GroupCombatCamera]::Active = $true
            $map = [object]::new(); [Verse.Find]::CurrentMap = $map; [LogSyncMod.AutoTest.CombatFightService]::Map = $map
            if ([CameraPlus.MarkerDecision].GetMethod('For').Invoke($null, @($settings, $config, $pawn)) -ne 32) { throw 'Failed compatibility changed the original camera method' }
        }
    }
    Write-Output "PASS: Camera+ $Mode; registration is nonfatal; warnings=$([Verse.Log]::Warnings.Count)"
}
finally { $harmony.UnpatchAll($harmony.Id) }
