param(
    [string]$OutputPath = "",
    [ValidateSet("All", "1.6", "1.7", "Tools")]
    [string]$TemplateGameVersion = "All",
    [string]$VersionSuffix = "",
    [switch]$CreateZip,
    [Parameter(DontShow = $true)][string]$SharedToolPath = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$templateRoot = Join-Path $projectRoot "components\ProjectTemplate"
$projectFile = Join-Path $projectRoot "RimDevTools\RimDevTools.csproj"
$projectXml = [xml](Get-Content -Raw -LiteralPath $projectFile)
$projectVersion = [string](($projectXml.Project.PropertyGroup | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.Version) } | Select-Object -First 1).Version)
$toolVersionValue = [System.Version]::Parse($projectVersion)
$toolVersion = "{0}.{1}.{2}.{3}" -f $toolVersionValue.Major, [Math]::Max(0, $toolVersionValue.Minor), [Math]::Max(0, $toolVersionValue.Build), [Math]::Max(0, $toolVersionValue.Revision)

if ($TemplateGameVersion -eq "All") {
    $commonTools = ""
    foreach ($gameVersion in @("1.7", "1.6", "Tools")) {
        $childParameters = @{ TemplateGameVersion = $gameVersion }
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $outputParent = Split-Path -Parent $OutputPath
            $outputName = Split-Path -Leaf $OutputPath
            $childParameters.OutputPath = Join-Path $outputParent ($outputName + "-RimWorld-" + $gameVersion)
        }
        if (-not [string]::IsNullOrWhiteSpace($VersionSuffix)) { $childParameters.VersionSuffix = $VersionSuffix }
        if ($CreateZip) { $childParameters.CreateZip = $true }
        if ($commonTools) { $childParameters.SharedToolPath = $commonTools }

        try { & $PSCommandPath @childParameters }
        catch { throw "RimWorld $gameVersion 模板打包失败: $($_.Exception.Message)" }
        if (-not $?) { throw "RimWorld $gameVersion 打包失败" }
        if (-not $commonTools) {
            $firstOutput = if ($childParameters.ContainsKey("OutputPath")) { $childParameters.OutputPath } else { "artifacts\RimDevTools-$toolVersion-Template-RimWorld-$gameVersion" }
            if ($VersionSuffix) { $firstOutput += "-$($VersionSuffix.Trim())" }
            $commonTools = Join-Path ([System.IO.Path]::GetFullPath((Join-Path $projectRoot $firstOutput))) ".tools"
        }
    }
    return
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $packageName = if ($TemplateGameVersion -eq "Tools") { "RimDevTools-$toolVersion-Tools" } else { "RimDevTools-$toolVersion-Template-RimWorld-$TemplateGameVersion" }
    $OutputPath = Join-Path "artifacts" $packageName
}
$output = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $OutputPath))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts"))

if (-not [string]::IsNullOrWhiteSpace($VersionSuffix)) {
    $VersionSuffix = $VersionSuffix.Trim()
    if ($VersionSuffix -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') {
        throw "版本后缀只能包含字母、数字、点、下划线和连字符: $VersionSuffix"
    }
    $outputParent = Split-Path -Parent $output
    $outputLeaf = Split-Path -Leaf $output
    $output = Join-Path $outputParent ($outputLeaf + "-" + $VersionSuffix)
}

if (-not ($output.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase))) {
    throw "发布输出必须位于 $artifactsRoot 内: $output"
}

$archive = $output + ".zip"
if (-not ($archive.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase))) {
    throw "发布压缩包必须位于 $artifactsRoot 内: $archive"
}

$temporary = Join-Path $artifactsRoot ".rimdevtools-package-build"
foreach ($ownedDirectory in @($output, $temporary)) {
    $ownedFullPath = [System.IO.Path]::GetFullPath($ownedDirectory)
    if (-not $ownedFullPath.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "发布清理目标越出 artifacts: $ownedFullPath"
    }
    $ownedAncestor = $ownedFullPath
    while ($ownedAncestor.Length -ge $artifactsRoot.Length) {
        if ((Test-Path -LiteralPath $ownedAncestor) -and ((Get-Item -LiteralPath $ownedAncestor -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
            throw "发布路径不能包含 Reparse Point: $ownedAncestor"
        }
        if ($ownedAncestor -eq $artifactsRoot) { break }
        $ownedAncestor = Split-Path -Parent $ownedAncestor
    }
}
$isToolsPackage = $TemplateGameVersion -eq "Tools"
$templateVersionRoot = Join-Path $templateRoot $TemplateGameVersion
$templateConfigName = if ($isToolsPackage) { "mod_config.tools.jsonc" } elseif ($TemplateGameVersion -eq "1.6") { "mod_config.jsonc" } else { "mod_config.$TemplateGameVersion.jsonc" }
$templateConfig = Join-Path $templateRoot $templateConfigName
$templateManifestName = if ($TemplateGameVersion -eq "1.6") { "manifest.json" } else { "manifest.$TemplateGameVersion.json" }
$templateManifestPath = Join-Path $templateRoot $templateManifestName

function Copy-DirectoryContents([string]$Source, [string]$Destination) {
    if (-not (Test-Path -LiteralPath $Source -PathType Container)) { throw "缺少目录: $Source" }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($entry in Get-ChildItem -LiteralPath $Source -Force) {
        if ($entry.PSIsContainer) {
            if ($entry.Name -in @(".git", ".vs", ".tools", ".rimdev", "artifacts", "bin", "obj")) { continue }
            Copy-DirectoryContents $entry.FullName (Join-Path $Destination $entry.Name)
        }
        else {
            Copy-Item -LiteralPath $entry.FullName -Destination $Destination -Force
        }
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
    if (-not $isToolsPackage -and -not (Test-Path -LiteralPath $templateVersionRoot -PathType Container)) { throw "缺少 RimWorld $TemplateGameVersion 模板目录: $templateVersionRoot" }
    if (-not $isToolsPackage -and -not (Test-Path -LiteralPath $templateManifestPath -PathType Leaf)) { throw "缺少 RimWorld $TemplateGameVersion 模板清单: $templateManifestPath" }
    if (Test-Path -LiteralPath (Join-Path $templateVersionRoot "mod_config.jsonc")) { throw "$TemplateGameVersion 模板目录内不得重复包含 mod_config.jsonc" }

    if ($isToolsPackage) {
        New-Item -ItemType Directory -Path $output -Force | Out-Null
        foreach ($client in @(".agents", ".claude")) {
            Copy-DirectoryContents (Join-Path $templateRoot "1.6\$client\skills") (Join-Path $output "$client\skills")
        }
    }
    else { Copy-DirectoryContents $templateVersionRoot $output }
    Copy-Item -LiteralPath $templateConfig -Destination (Join-Path $output "mod_config.jsonc") -Force
    $claudeLocalSettings = Join-Path $output ".claude\settings.local.json"
    if (Test-Path -LiteralPath $claudeLocalSettings) { Remove-Item -LiteralPath $claudeLocalSettings -Force }

    $toolRoot = Join-Path $output ".tools"
    $dataRoot = Join-Path $toolRoot "Data"
    $logSyncComponent = Join-Path $dataRoot "LogSyncMod"
    $logRoot = Join-Path $toolRoot "log"
    $autoTestOutput = Join-Path $logRoot "AutoTestOutput"
    $modListRoot = Join-Path $toolRoot "modlist"
    New-Item -ItemType Directory -Path $toolRoot, $dataRoot -Force | Out-Null
    if (-not $isToolsPackage) { New-Item -ItemType Directory -Path $autoTestOutput -Force | Out-Null }
    if ($SharedToolPath) {
        $sharedFull = [System.IO.Path]::GetFullPath($SharedToolPath)
        if (-not $sharedFull.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) { throw "共用工具路径必须位于 artifacts" }
        Copy-Item -LiteralPath (Join-Path $sharedFull "RimDevTools.exe") -Destination $toolRoot -Force
        foreach ($part in @("Data", "Ilspy", "modlist")) { Copy-DirectoryContents (Join-Path $sharedFull $part) (Join-Path $toolRoot $part) }
        $copiedTemplateMetadata = Join-Path $dataRoot "project-template.manifest.json"
        if (Test-Path -LiteralPath $copiedTemplateMetadata) { Remove-Item -LiteralPath $copiedTemplateMetadata -Force }
    }
    else {
        New-Item -ItemType Directory -Path $logSyncComponent, $modListRoot -Force | Out-Null

        $rimDevToolsPublish = Join-Path $temporary "RimDevTools"
        dotnet publish (Join-Path $projectRoot "RimDevTools\RimDevTools.csproj") -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $rimDevToolsPublish
        if ($LASTEXITCODE -ne 0) { throw "RimDevTools publish 失败" }
        Copy-Item -LiteralPath (Join-Path $rimDevToolsPublish "RimDevTools.exe") -Destination (Join-Path $toolRoot "RimDevTools.exe") -Force

        if (-not $SharedToolPath) {
            dotnet build (Join-Path $projectRoot "source\AutoTestRunner\AutoTestRunner.1.7.csproj") -c Release -t:Rebuild
            if ($LASTEXITCODE -ne 0) { throw "RimWorld 1.7 LogSyncMod / AutoTest build 失败" }
        }
        # Other game versions retain their tracked binaries; never compile legacy references into 1.7.

        Copy-Item -LiteralPath (Join-Path $projectRoot "About") -Destination (Join-Path $logSyncComponent "About") -Recurse -Force
        Copy-Item -LiteralPath (Join-Path $projectRoot "Languages") -Destination (Join-Path $logSyncComponent "Languages") -Recurse -Force
        Copy-Item -LiteralPath (Join-Path $projectRoot "LoadFolders.xml") -Destination $logSyncComponent -Force
        foreach ($gameVersion in @("1.6", "1.7")) {
            $sourceAssemblies = Join-Path $projectRoot "$gameVersion\Assemblies"
            if (-not (Test-Path -LiteralPath $sourceAssemblies -PathType Container)) { throw "缺少 $gameVersion LogSyncMod 程序集目录: $sourceAssemblies" }
            $destinationAssemblies = Join-Path $logSyncComponent "$gameVersion\Assemblies"
            New-Item -ItemType Directory -Path $destinationAssemblies -Force | Out-Null
            foreach ($assembly in @("LogSyncMod.dll", "AutoTestRunner.dll")) {
                $sourceAssembly = Join-Path $sourceAssemblies $assembly
                if (-not (Test-Path -LiteralPath $sourceAssembly -PathType Leaf)) { throw "缺少 $gameVersion 程序集: $sourceAssembly" }
                Copy-Item -LiteralPath $sourceAssembly -Destination $destinationAssemblies -Force
            }
            $sourceDefs = Join-Path $projectRoot "$gameVersion\Defs"
            if (Test-Path -LiteralPath $sourceDefs -PathType Container) {
                Copy-Item -LiteralPath $sourceDefs -Destination (Join-Path $logSyncComponent "$gameVersion\Defs") -Recurse -Force
            }
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
    }
    if (-not $isToolsPackage) {
        [System.IO.File]::WriteAllText((Join-Path $logRoot "精选日志.log"), "", [System.Text.UTF8Encoding]::new($false))

        $layoutManifest = Get-Content -Raw -LiteralPath $templateManifestPath | ConvertFrom-Json
        if ($layoutManifest.schemaVersion -ne 2) { throw "发布布局清单 schemaVersion 必须为 2" }
        if ($layoutManifest.supportedVersions -notcontains $TemplateGameVersion) { throw "模板清单不声明支持 RimWorld $TemplateGameVersion" }
        $templateMetadata = [ordered]@{
            schemaVersion = 1
            templateGameVersion = $TemplateGameVersion
            templateSchemaVersion = [int]$layoutManifest.schemaVersion
        }
        [System.IO.File]::WriteAllText(
            (Join-Path $dataRoot "project-template.manifest.json"),
            ($templateMetadata | ConvertTo-Json -Depth 4),
            [System.Text.UTF8Encoding]::new($false))
        # IDE project discovery can restore the freshly copied template while the tools build.
        # Remove only this package's known generated directories before validating the payload.
        foreach ($generatedRelative in @("source\MyCustomMod\obj", "source\MyCustomMod\bin")) {
            $generatedPath = [System.IO.Path]::GetFullPath((Join-Path $output $generatedRelative))
            if (-not $generatedPath.StartsWith($output + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) { throw "生成目录越出发布包: $generatedPath" }
            if (Test-Path -LiteralPath $generatedPath) {
                if ((Get-Item -LiteralPath $generatedPath -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw "生成目录不能是 Reparse Point: $generatedPath" }
                Remove-Item -LiteralPath $generatedPath -Recurse -Force
            }
        }

    }

    # Only tool-owned files belong to the update baseline. Config is a reference,
    # and template identity must retain its original meaning after tool updates.
    $managedFiles = @()
    $managedFiles += Get-Item -LiteralPath (Join-Path $toolRoot "RimDevTools.exe")
    foreach ($part in @("Data", "Ilspy", "modlist")) {
        $managedFiles += Get-ChildItem -LiteralPath (Join-Path $toolRoot $part) -Recurse -File -Force | Where-Object { $_.Name -notin @("project-template.manifest.json", "tool-package.manifest.json") }
    }
    foreach ($client in @(".agents", ".claude")) {
        $managedFiles += Get-ChildItem -LiteralPath (Join-Path $output "$client\skills\rimdevtools") -Recurse -File -Force
    }
    $fileRecords = @($managedFiles | Sort-Object { Get-RelativePath $output $_.FullName } | ForEach-Object {
        $relative = (Get-RelativePath $output $_.FullName).Replace('\', '/')
        $role = if ($relative -like "*/skills/*") { "skill" } elseif ($relative -eq ".tools/RimDevTools.exe") { "executable" } elseif ($relative -like ".tools/Data/licenses/*") { "license" } elseif ($relative -like ".tools/modlist/*") { "preset" } else { "component" }
        [ordered]@{ path = $relative; size = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); role = $role; updatePolicy = "replace" }
    })
    $packageManifest = [ordered]@{
        schemaVersion = 1; toolVersion = $toolVersion; platform = "win-x64"; runtimeMajor = 10
        supportedGameVersions = @("1.6", "1.7"); configVersion = 2
        configDefaults = [ordered]@{ existing_project_mode = $false }
        files = $fileRecords
    }
    [System.IO.File]::WriteAllText((Join-Path $dataRoot "tool-package.manifest.json"), ($packageManifest | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
    if ($SharedToolPath) {
        $originalManifest = Get-Content -Raw -LiteralPath (Join-Path $SharedToolPath "Data\tool-package.manifest.json")
        if ($originalManifest -cne (Get-Content -Raw -LiteralPath (Join-Path $dataRoot "tool-package.manifest.json"))) { throw "三包的共用工具内容不一致" }
    }
    if ($isToolsPackage) { $layoutManifest = Get-Content -Raw -LiteralPath (Join-Path $templateRoot "manifest.tools.json") | ConvertFrom-Json }
    foreach ($relative in $layoutManifest.requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $output $relative) -PathType Leaf)) { throw "发布包缺少必需文件: $relative" }
    }
    foreach ($relative in $layoutManifest.requiredDirectories) {
        if (-not (Test-Path -LiteralPath (Join-Path $output $relative) -PathType Container)) { throw "发布包缺少必需目录: $relative" }
    }
    foreach ($relative in $layoutManifest.forbiddenPaths) {
        if (Test-Path -LiteralPath (Join-Path $output $relative)) { throw "发布包包含禁止路径: $relative" }
    }
    if (Test-Path -LiteralPath (Join-Path $toolRoot "ProjectData")) { throw "发行包不得包含项目运行数据" }
    $officialLibraryNames = @("Assembly-CSharp.dll", "UnityStubs.dll", "Waylith.dll", "Waylith.AssetBake.dll", "steam_api64.dll")
    foreach ($file in Get-ChildItem -LiteralPath $output -Recurse -File) {
        if ($file.Name -in $officialLibraryNames) { throw "发行包不得携带官方库: $($file.FullName)" }
        if ($file.Name -in @("deployment-receipt.json", "steamworkshops-search-cache.json", "steamworkshops-bindings.json")) { throw "发行包不得携带运行历史: $($file.FullName)" }
    }

    $versionJson = (& (Join-Path $toolRoot "RimDevTools.exe") --version | Out-String)
    if ($LASTEXITCODE -ne 0) { throw "发布后的 RimDevTools --version 验证失败" }
    $version = $versionJson | ConvertFrom-Json
    if (-not $version.ok -or [string]::IsNullOrWhiteSpace([string]$version.rimDevToolsVersion)) { throw "发布后的 RimDevTools 版本输出无效: $versionJson" }
    $expectedGame = if ($isToolsPackage) { "unpackaged" } else { $TemplateGameVersion }
    $expectedSchema = if ($isToolsPackage) { 0 } else { $layoutManifest.schemaVersion }
    if ($version.templateGameVersion -ne $expectedGame -or $version.templateSchemaVersion -ne $expectedSchema) { throw "发布后的模板版本信息不匹配（期望 RimWorld $TemplateGameVersion / schema $($layoutManifest.schemaVersion)）: $versionJson" }
    Write-Host $versionJson.Trim()
}
finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}

Write-Host "发布完成: $output"

if ($CreateZip) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
    # Windows PowerShell can enable the legacy backslash ZIP path switch.
    # Write portable names explicitly so existing updaters accept the archive.
    $archiveStream = [System.IO.File]::Create($archive)
    $zip = $null
    try {
        $zip = [System.IO.Compression.ZipArchive]::new($archiveStream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
        foreach ($entry in Get-ChildItem -LiteralPath $output -Recurse -Force | Sort-Object FullName) {
            $relative = (Get-RelativePath $output $entry.FullName).Replace('\', '/')
            if ($entry.PSIsContainer) { $null = $zip.CreateEntry($relative.TrimEnd('/') + '/'); continue }
            $zipEntry = $zip.CreateEntry($relative, [System.IO.Compression.CompressionLevel]::Optimal)
            $inputStream = [System.IO.File]::OpenRead($entry.FullName)
            try {
                $entryStream = $zipEntry.Open()
                try { $inputStream.CopyTo($entryStream) }
                finally { $entryStream.Dispose() }
            }
            finally { $inputStream.Dispose() }
        }
    }
    finally {
        if ($null -ne $zip) { $zip.Dispose() }
        $archiveStream.Dispose()
    }
    Write-Host "压缩包已生成: $archive"
}
