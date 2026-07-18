# 自动编译并打包 Shader AssetBundle

$ConfigFile = Join-Path $PSScriptRoot "..\..\mod_config.jsonc"
if (-not (Test-Path $ConfigFile)) {
    Write-Host "[错误] 未找到配置文件: $ConfigFile" -ForegroundColor Red
    exit 1
}

$raw = Get-Content $ConfigFile -Raw -Encoding UTF8
$cleaned = $raw -replace '(?m)//.*$', ''
$Config = $cleaned | ConvertFrom-Json
$ProjectRoot = [System.IO.Path]::GetFullPath((Split-Path $ConfigFile -Parent))
$ShaderConfig = $Config.shader_bundle

if ($null -eq $ShaderConfig -or -not $ShaderConfig.enabled) {
    Write-Host "未启用 shader bundle 构建，跳过。" -ForegroundColor Yellow
    exit 0
}

$UnityProjectPath = [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot $ShaderConfig.unity_project_path))
$UnityExePath = $ShaderConfig.unity_exe_path
$BuildMethod = $ShaderConfig.build_method
$LogPath = Join-Path $UnityProjectPath "build_shader_bundle.log"
$BundleName = $ShaderConfig.bundle_name
$BundleOutputPath = Join-Path $ProjectRoot "AssetBundles\$BundleName"

if (-not (Test-Path $UnityProjectPath)) {
    Write-Host "[错误] Unity 工程不存在: $UnityProjectPath" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $UnityExePath)) {
    Write-Host "[错误] Unity 编辑器不存在: $UnityExePath" -ForegroundColor Red
    exit 1
}

if (Test-Path $LogPath) {
    Remove-Item -LiteralPath $LogPath -Force
}

Write-Host ">>> 开始构建 shader bundle..." -ForegroundColor Cyan
Write-Host "  Unity: $UnityExePath" -ForegroundColor DarkGray
Write-Host "  Project: $UnityProjectPath" -ForegroundColor DarkGray
Write-Host "  Method: $BuildMethod" -ForegroundColor DarkGray

$process = Start-Process -FilePath $UnityExePath -ArgumentList @(
    "-batchmode",
    "-nographics",
    "-quit",
    "-projectPath", "`"$UnityProjectPath`"",
    "-executeMethod", $BuildMethod,
    "-logFile", "`"$LogPath`""
) -PassThru -NoNewWindow -Wait

if ($process.ExitCode -ne 0) {
    Write-Host "[错误] Unity 进程退出码异常: $($process.ExitCode)" -ForegroundColor Red
    if (Test-Path $LogPath) {
        Write-Host "最近日志:" -ForegroundColor Yellow
        Get-Content -Path $LogPath -Tail 30
    }
    exit $process.ExitCode
}

if (-not (Test-Path $BundleOutputPath)) {
    Write-Host "[错误] 未找到输出 bundle: $BundleOutputPath" -ForegroundColor Red
    if (Test-Path $LogPath) {
        Write-Host "最近日志:" -ForegroundColor Yellow
        Get-Content -Path $LogPath -Tail 30
    }
    exit 1
}

Write-Host "[OK] Shader bundle 构建完成: $BundleOutputPath" -ForegroundColor Green
