param(
    [switch]$SyncSource
)

$ProjectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$RimDevToolsExe = Join-Path $PSScriptRoot "RimDevTools.exe"
$RimDevToolsProject = Join-Path $ProjectRoot "RimDevTools\RimDevTools.csproj"
$DeployArguments = @("deploy")
if ($SyncSource) {
    $DeployArguments += "--sync-source"
}

$ExitCode = 1
Push-Location $ProjectRoot
try {
    if (Test-Path -LiteralPath $RimDevToolsProject -PathType Leaf) {
        & dotnet run --no-launch-profile --project $RimDevToolsProject -- @DeployArguments
        $ExitCode = $LASTEXITCODE
    }
    elseif (Test-Path -LiteralPath $RimDevToolsExe -PathType Leaf) {
        & $RimDevToolsExe @DeployArguments
        $ExitCode = $LASTEXITCODE
    }
    else {
        Write-Error "未找到 RimDevTools.exe 或 RimDevTools.csproj。"
    }
}
catch {
    Write-Error $_
}
finally {
    Pop-Location
}

exit $ExitCode
