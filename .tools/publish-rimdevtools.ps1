param(
    [string]$OutputPath = "artifacts\RimDevTools-Package"
)

$ErrorActionPreference = "Stop"
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$output = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $OutputPath))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts"))
if (-not ($output.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase))) {
    throw "发布输出必须位于 $artifactsRoot 内: $output"
}

$temporary = Join-Path $artifactsRoot ".rimdevtools-package-build"
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }

$toolRoot = Join-Path $output ".tools"
$dataRoot = Join-Path $toolRoot "Data"
$component = Join-Path $dataRoot "RimSearcher_DataMod"
$logRoot = Join-Path $toolRoot "log"
$autoTestOutput = Join-Path $logRoot "AutoTestOutput"
$modListRoot = Join-Path $toolRoot "modlist"
$databaseRoot = Join-Path $toolRoot "db"
$projectTemplateSource = Join-Path $projectRoot "components\ProjectTemplate"

try {
    New-Item -ItemType Directory -Path $toolRoot, $dataRoot, $component, $autoTestOutput, $modListRoot, $databaseRoot -Force | Out-Null

    $rimDevToolsPublish = Join-Path $temporary "RimDevTools"
    dotnet publish (Join-Path $projectRoot "RimDevTools\RimDevTools.csproj") -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $rimDevToolsPublish
    if ($LASTEXITCODE -ne 0) { throw "RimDevTools publish 失败" }
    Copy-Item -LiteralPath (Join-Path $rimDevToolsPublish "RimDevTools.exe") -Destination (Join-Path $toolRoot "RimDevTools.exe") -Force

    $rimSearcherPublish = Join-Path $temporary "RimSearcher"
    dotnet publish (Join-Path $projectRoot "RimSearcher\Sources\RimSearcher.Cli\RimSearcher.Cli.csproj") -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $rimSearcherPublish
    if ($LASTEXITCODE -ne 0) { throw "RimSearcher publish 失败" }
    Copy-Item -LiteralPath (Join-Path $rimSearcherPublish "rimsearcher.exe") -Destination (Join-Path $toolRoot "rimsearcher.exe") -Force

    dotnet build (Join-Path $projectRoot "RimSearcher\Sources\RimSearcher.DataMod\RimSearcher.DataMod.csproj") -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "RimSearcher DataMod build 失败" }
    foreach ($folder in @("About", "Assemblies", "Languages", "Native")) {
        Copy-Item -LiteralPath (Join-Path $projectRoot "RimSearcher\RimSearcher_DataMod\$folder") -Destination (Join-Path $component $folder) -Recurse -Force
    }

    $ilspySource = Join-Path $projectRoot ".tools\ilspy"
    if (-not (Test-Path -LiteralPath (Join-Path $ilspySource "ilspycmd.exe"))) { throw "缺少 ILSpy 发布组件: $ilspySource" }
    Copy-Item -LiteralPath $ilspySource -Destination (Join-Path $toolRoot "Ilspy") -Recurse -Force

    $licenses = Join-Path $dataRoot "licenses"
    New-Item -ItemType Directory -Path $licenses -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot "RimSearcher\LICENSE") -Destination (Join-Path $licenses "RimSearcher-LICENSE.txt") -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot "RimDevTools\Packaging\licenses\ILSpy-LICENSE.txt") -Destination $licenses -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot "RimDevTools\Packaging\licenses\third-party-notices.txt") -Destination $licenses -Force

    Copy-Item -Path (Join-Path $projectRoot ".tools\test\modlist\*.xml") -Destination $modListRoot -Force
    Copy-Item -LiteralPath (Join-Path $projectTemplateSource "1.6\mod_config.jsonc") -Destination (Join-Path $output "mod_config.jsonc") -Force
    [System.IO.File]::WriteAllText((Join-Path $logRoot "精选日志.log"), "", [System.Text.UTF8Encoding]::new($false))

    $projectTemplateTarget = Join-Path $toolRoot "components\ProjectTemplate"
    if (-not (Test-Path -LiteralPath (Join-Path $projectTemplateSource "manifest.json"))) {
        throw "缺少 ProjectTemplate manifest: $projectTemplateSource"
    }
    New-Item -ItemType Directory -Path $projectTemplateTarget -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectTemplateSource "manifest.json") -Destination $projectTemplateTarget -Force
    foreach ($version in @("1.6")) {
        $sourceVersion = Join-Path $projectTemplateSource $version
        if (-not (Test-Path -LiteralPath $sourceVersion)) { throw "缺少 ProjectTemplate $version 模板: $sourceVersion" }
        $targetVersion = Join-Path $projectTemplateTarget $version
        Copy-Item -LiteralPath $sourceVersion -Destination $targetVersion -Recurse -Force
        $claudeLocalSettings = Join-Path $targetVersion ".claude\settings.local.json"
        if (Test-Path -LiteralPath $claudeLocalSettings) {
            Remove-Item -LiteralPath $claudeLocalSettings -Force
        }
        if (Test-Path -LiteralPath (Join-Path $targetVersion ".tools")) {
            throw "ProjectTemplate $version 不得重复包含 .tools"
        }
    }

    $versionJson = (& (Join-Path $toolRoot "RimDevTools.exe") --version | Out-String)
    if ($LASTEXITCODE -ne 0) { throw "发布后的 RimDevTools --version 验证失败" }
    $version = $versionJson | ConvertFrom-Json
    if (-not $version.componentsCompatible) { throw "发布组件版本不兼容: $versionJson" }
    Write-Host $versionJson.Trim()
}
finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}

Write-Host "发布完成: $output"
