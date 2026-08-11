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
$templateRoot = Join-Path $projectRoot "components\ProjectTemplate"
$templateVersionRoot = Join-Path $templateRoot "1.6"
$templateConfig = Join-Path $templateRoot "mod_config.jsonc"

function Copy-DirectoryContents([string]$Source, [string]$Destination) {
    if (-not (Test-Path -LiteralPath $Source -PathType Container)) { throw "缺少目录: $Source" }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($entry in Get-ChildItem -LiteralPath $Source -Force) {
        Copy-Item -LiteralPath $entry.FullName -Destination (Join-Path $Destination $entry.Name) -Recurse -Force
    }
}

function Get-RelativePath([string]$Root, [string]$Path) {
    $rootUri = [System.Uri]::new(([System.IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'))
    $pathUri = [System.Uri]::new([System.IO.Path]::GetFullPath($Path))
    return [System.Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString())
}

function Get-DirectoryContentHash([string]$Root) {
    $fullRoot = [System.IO.Path]::GetFullPath($Root)
    $hash = [System.Security.Cryptography.IncrementalHash]::CreateHash([System.Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        $files = Get-ChildItem -LiteralPath $fullRoot -Recurse -File | Sort-Object {
            (Get-RelativePath $fullRoot $_.FullName).Replace('\', '/').ToUpperInvariant()
        }
        foreach ($file in $files) {
            if (($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "工具 Mod 组件内不得包含 Reparse Point: $($file.FullName)"
            }
            $relative = (Get-RelativePath $fullRoot $file.FullName).Replace('\', '/')
            $hash.AppendData([System.Text.Encoding]::UTF8.GetBytes($relative))
            $hash.AppendData([byte[]]@(0))
            $hash.AppendData([System.BitConverter]::GetBytes([int64]$file.Length))
            $hash.AppendData([byte[]]@(0))
            $stream = [System.IO.File]::OpenRead($file.FullName)
            try {
                $buffer = [byte[]]::new(65536)
                while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    $hash.AppendData($buffer, 0, $read)
                }
            }
            finally { $stream.Dispose() }
            $hash.AppendData([byte[]]@(0))
        }
        return "sha256:" + [System.BitConverter]::ToString($hash.GetHashAndReset()).Replace("-", "").ToLowerInvariant()
    }
    finally { $hash.Dispose() }
}

function Get-ModVersion([string]$ModRoot, [string]$PrimaryAssembly) {
    [xml]$about = Get-Content -Raw -LiteralPath (Join-Path $ModRoot "About\About.xml")
    $aboutVersion = [string]$about.ModMetaData.modVersion
    $assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName((Join-Path $ModRoot $PrimaryAssembly)).Version.ToString()
    if ([string]::IsNullOrWhiteSpace($aboutVersion)) { throw "Mod 缺少 modVersion: $ModRoot" }
    $aboutParsed = [System.Version]$aboutVersion
    $assemblyParsed = [System.Version]$assemblyVersion
    if ($aboutParsed.Major -ne $assemblyParsed.Major -or $aboutParsed.Minor -ne $assemblyParsed.Minor -or $aboutParsed.Build -ne $assemblyParsed.Build) {
        throw "Mod About 与主程序集版本不一致: $ModRoot ($aboutVersion / $assemblyVersion)"
    }
    return $aboutVersion
}

if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }

try {
    if (-not (Test-Path -LiteralPath $templateConfig -PathType Leaf)) { throw "缺少通用项目配置: $templateConfig" }
    if (Test-Path -LiteralPath (Join-Path $templateVersionRoot "mod_config.jsonc")) { throw "1.6 模板内不得重复包含 mod_config.jsonc" }

    Copy-DirectoryContents $templateVersionRoot $output
    Copy-Item -LiteralPath $templateConfig -Destination (Join-Path $output "mod_config.jsonc") -Force
    $claudeLocalSettings = Join-Path $output ".claude\settings.local.json"
    if (Test-Path -LiteralPath $claudeLocalSettings) { Remove-Item -LiteralPath $claudeLocalSettings -Force }

    $toolRoot = Join-Path $output ".tools"
    $dataRoot = Join-Path $toolRoot "Data"
    $logSyncComponent = Join-Path $dataRoot "LogSyncMod"
    $logRoot = Join-Path $toolRoot "log"
    $autoTestOutput = Join-Path $logRoot "AutoTestOutput"
    $modListRoot = Join-Path $toolRoot "modlist"
    New-Item -ItemType Directory -Path $toolRoot, $dataRoot, $logSyncComponent, $autoTestOutput, $modListRoot -Force | Out-Null

    $rimDevToolsPublish = Join-Path $temporary "RimDevTools"
    dotnet publish (Join-Path $projectRoot "RimDevTools\RimDevTools.csproj") -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $rimDevToolsPublish
    if ($LASTEXITCODE -ne 0) { throw "RimDevTools publish 失败" }
    Copy-Item -LiteralPath (Join-Path $rimDevToolsPublish "RimDevTools.exe") -Destination (Join-Path $toolRoot "RimDevTools.exe") -Force

    dotnet build (Join-Path $projectRoot "source\LogSyncMod\LogSyncMod.csproj") -c Release -t:Rebuild
    if ($LASTEXITCODE -ne 0) { throw "LogSyncMod build 失败" }
    dotnet build (Join-Path $projectRoot "source\AutoTestRunner\AutoTestRunner.csproj") -c Release -t:Rebuild
    if ($LASTEXITCODE -ne 0) { throw "LogSyncMod AutoTest build 失败" }

    Copy-Item -LiteralPath (Join-Path $projectRoot "About") -Destination (Join-Path $logSyncComponent "About") -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot "Languages") -Destination (Join-Path $logSyncComponent "Languages") -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot "LoadFolders.xml") -Destination $logSyncComponent -Force
    $logSyncAssemblies = Join-Path $logSyncComponent "1.6\Assemblies"
    New-Item -ItemType Directory -Path $logSyncAssemblies -Force | Out-Null
    foreach ($assembly in @("LogSyncMod.dll", "AutoTestRunner.dll")) {
        Copy-Item -LiteralPath (Join-Path $projectRoot "1.6\Assemblies\$assembly") -Destination $logSyncAssemblies -Force
    }
    if (Test-Path -LiteralPath (Join-Path $projectRoot "1.6\Defs")) {
        Copy-Item -LiteralPath (Join-Path $projectRoot "1.6\Defs") -Destination (Join-Path $logSyncComponent "1.6\Defs") -Recurse -Force
    }

    $logSyncVersion = Get-ModVersion $logSyncComponent "1.6\Assemblies\LogSyncMod.dll"
    $autoTestVersion = [System.Reflection.AssemblyName]::GetAssemblyName((Join-Path $logSyncComponent "1.6\Assemblies\AutoTestRunner.dll")).Version.ToString()
    $logSyncVersionParsed = [System.Version]$logSyncVersion
    $autoTestVersionParsed = [System.Version]$autoTestVersion
    if ($logSyncVersionParsed.Major -ne $autoTestVersionParsed.Major -or $logSyncVersionParsed.Minor -ne $autoTestVersionParsed.Minor -or $logSyncVersionParsed.Build -ne $autoTestVersionParsed.Build) { throw "LogSyncMod 与 AutoTest 程序集版本不一致" }
    $manifest = [ordered]@{
        schemaVersion = 1
        components = @(
            [ordered]@{
                folder = "LogSyncMod"
                packageId = "trigger.logsyncmod"
                version = $logSyncVersion
                primaryAssembly = "1.6/Assemblies/LogSyncMod.dll"
                contentHash = Get-DirectoryContentHash $logSyncComponent
            }
        )
    }
    [System.IO.File]::WriteAllText((Join-Path $dataRoot "tool-mods.manifest.json"), ($manifest | ConvertTo-Json -Depth 5), [System.Text.UTF8Encoding]::new($false))

    $ilspySource = Join-Path $projectRoot ".tools\ilspy"
    if (-not (Test-Path -LiteralPath (Join-Path $ilspySource "ilspycmd.exe"))) { throw "缺少 ILSpy 发布组件: $ilspySource" }
    Copy-Item -LiteralPath $ilspySource -Destination (Join-Path $toolRoot "Ilspy") -Recurse -Force

    $licenses = Join-Path $dataRoot "licenses"
    New-Item -ItemType Directory -Path $licenses -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot "RimDevTools\Packaging\licenses\ILSpy-LICENSE.txt") -Destination $licenses -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot "RimDevTools\Packaging\licenses\third-party-notices.txt") -Destination $licenses -Force

    Copy-Item -Path (Join-Path $projectRoot ".tools\test\modlist\*.xml") -Destination $modListRoot -Force
    [System.IO.File]::WriteAllText((Join-Path $logRoot "精选日志.log"), "", [System.Text.UTF8Encoding]::new($false))

    $layoutManifest = Get-Content -Raw -LiteralPath (Join-Path $templateRoot "manifest.json") | ConvertFrom-Json
    if ($layoutManifest.schemaVersion -ne 2) { throw "发布布局清单 schemaVersion 必须为 2" }
    foreach ($relative in $layoutManifest.requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $output $relative) -PathType Leaf)) { throw "发布包缺少必需文件: $relative" }
    }
    foreach ($relative in $layoutManifest.requiredDirectories) {
        if (-not (Test-Path -LiteralPath (Join-Path $output $relative) -PathType Container)) { throw "发布包缺少必需目录: $relative" }
    }
    foreach ($relative in $layoutManifest.forbiddenPaths) {
        if (Test-Path -LiteralPath (Join-Path $output $relative)) { throw "发布包包含禁止路径: $relative" }
    }

    $versionJson = (& (Join-Path $toolRoot "RimDevTools.exe") --version | Out-String)
    if ($LASTEXITCODE -ne 0) { throw "发布后的 RimDevTools --version 验证失败" }
    $version = $versionJson | ConvertFrom-Json
    if (-not $version.ok -or [string]::IsNullOrWhiteSpace([string]$version.rimDevToolsVersion)) { throw "发布后的 RimDevTools 版本输出无效: $versionJson" }
    Write-Host $versionJson.Trim()
}
finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}

Write-Host "发布完成: $output"
