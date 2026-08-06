param(
    [string]$OutputPath = "artifacts\RimDevTools"
)

$ErrorActionPreference = "Stop"
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$output = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $OutputPath))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts"))
if (-not ($output.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase))) {
    throw "发布输出必须位于 $artifactsRoot 内: $output"
}

if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$rimSearcherTemp = Join-Path $artifactsRoot ".rimsearcher-publish"
if (Test-Path -LiteralPath $rimSearcherTemp) { Remove-Item -LiteralPath $rimSearcherTemp -Recurse -Force }

try {
    dotnet publish (Join-Path $projectRoot "RimDevTools\RimDevTools.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $output
    if ($LASTEXITCODE -ne 0) { throw "RimDevTools publish 失败" }

    dotnet publish (Join-Path $projectRoot "RimSearcher\Sources\RimSearcher.Cli\RimSearcher.Cli.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $rimSearcherTemp
    if ($LASTEXITCODE -ne 0) { throw "RimSearcher publish 失败" }
    Copy-Item -LiteralPath (Join-Path $rimSearcherTemp "rimsearcher.exe") -Destination (Join-Path $output "rimsearcher.exe") -Force

    dotnet build (Join-Path $projectRoot "RimSearcher\Sources\RimSearcher.DataMod\RimSearcher.DataMod.csproj") -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "RimSearcher DataMod build 失败" }
    $component = Join-Path $output "components\RimSearcher_DataMod"
    foreach ($folder in @("About", "Assemblies", "Languages", "Native")) {
        Copy-Item -LiteralPath (Join-Path $projectRoot "RimSearcher\RimSearcher_DataMod\$folder") -Destination (Join-Path $component $folder) -Recurse -Force
    }

    $ilspySource = Join-Path $projectRoot ".tools\ilspy"
    if (-not (Test-Path -LiteralPath (Join-Path $ilspySource "ilspycmd.exe"))) { throw "缺少 ILSpy 发布组件: $ilspySource" }
    Copy-Item -LiteralPath $ilspySource -Destination (Join-Path $output "tools\ilspycmd") -Recurse -Force

    $licenses = Join-Path $output "licenses"
    New-Item -ItemType Directory -Path $licenses -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot "RimSearcher\LICENSE") -Destination (Join-Path $licenses "RimSearcher-LICENSE.txt") -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot "RimDevTools\Packaging\licenses\ILSpy-LICENSE.txt") -Destination $licenses -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot "RimDevTools\Packaging\licenses\third-party-notices.txt") -Destination $licenses -Force

    $versionJson = (& (Join-Path $output "RimDevTools.exe") --version | Out-String)
    if ($LASTEXITCODE -ne 0) { throw "发布后的 RimDevTools --version 验证失败" }
    $version = $versionJson | ConvertFrom-Json
    if (-not $version.componentsCompatible) { throw "发布组件版本不兼容: $versionJson" }
    Write-Host $versionJson.Trim()
}
finally {
    if (Test-Path -LiteralPath $rimSearcherTemp) { Remove-Item -LiteralPath $rimSearcherTemp -Recurse -Force }
}

Write-Host "发布完成: $output"
