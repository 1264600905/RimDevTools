# 自动化编译部署脚本
# 所有可变配置统一从项目根目录的 mod_config.jsonc 读取，无需修改此脚本。
# 使用须知:
#   1. 别再用 TempProject 去部署了好嘛？dll 必须不能叫 TempProject，在第一次部署时必须改成模组相关名称。
#      此脚本可能会在很多模组使用，如果 dll 都一样了必定报错。
#   2. 任何修改后执行部署脚本是标准的执行流程，不需要专门问，修改完毕必须执行部署。

param(
    [switch]$SyncSource  # 是否同步 source 文件夹到部署目录
)

# ==========================================
# 1. 从 mod_config.jsonc 加载配置
# ==========================================

$ConfigFile = Join-Path $PSScriptRoot "..\mod_config.jsonc"
if (-not (Test-Path $ConfigFile)) {
    Write-Host "[错误] 未找到配置文件: $ConfigFile" -ForegroundColor Red
    exit 1
}
$raw = Get-Content $ConfigFile -Raw -Encoding UTF8
$cleaned = $raw -replace '(?m)//.*$', ''
$Config = $cleaned | ConvertFrom-Json

# 项目根目录（mod_config.jsonc 所在目录）
$ProjectRoot = [System.IO.Path]::GetFullPath((Split-Path $ConfigFile -Parent))

# 从配置读取各项参数
$ModName = $Config.mod_name
$RimWorldModDir = Join-Path $Config.rimworld_dir "Mods"
$CsprojPaths = @($Config.csproj_paths | ForEach-Object { Join-Path $ProjectRoot $_ })
$SyncItems = @($Config.sync_items)
$ProtectedFilesRegex = $Config.protected_files_regex
$ShaderBundleConfig = $Config.shader_bundle

# 如果指定了 -SyncSource 参数，则将 source 文件夹加入同步列表
if ($SyncSource) {
    Write-Host "已启用 source 同步模式，source 文件夹将被部署到目标目录。" -ForegroundColor Magenta
    $SyncItems += "source"
}

# ==========================================
# 2. 内部逻辑部分 (通常不需要修改)
# ==========================================

$devPath = $ProjectRoot
$modPath = Join-Path $RimWorldModDir $ModName

function Get-RimWorldProcesses {
    Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $_.ProcessName -like "RimWorld*"
    }
}

function Wait-ForRimWorldToClose {
    param(
        [int]$TimeoutSeconds = 600,
        [int]$StableClosedSeconds = 10
    )

    Write-Host ">>> 步骤 0: 检查 RimWorld 运行状态..." -ForegroundColor Gray

    $initialProcesses = @(Get-RimWorldProcesses)
    if ($initialProcesses.Count -eq 0) {
        Write-Host "  未检测到 RimWorld，直接开始部署。" -ForegroundColor Green
        return
    }

    $initialProcessInfo = ($initialProcesses | Select-Object -ExpandProperty Id) -join ", "
    Write-Host "  检测到 RimWorld 正在运行，PID: $initialProcessInfo。将等待其关闭并稳定 ${StableClosedSeconds}s 后再部署。" -ForegroundColor Yellow

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $remainingSeconds = [Math]::Max(0, [int][Math]::Floor(($deadline - (Get-Date)).TotalSeconds))
        $processes = @(Get-RimWorldProcesses)
        if ($processes.Count -gt 0) {
            $processInfo = ($processes | Select-Object -ExpandProperty Id) -join ", "
            Write-Host "  检测到 RimWorld 正在运行，PID: $processInfo。等待关闭，剩余超时 ${remainingSeconds}s..." -ForegroundColor Yellow
            Start-Sleep -Seconds 2
            continue
        }

        Write-Host "  RimWorld 已关闭，开始等待 ${StableClosedSeconds}s 稳定窗口..." -ForegroundColor DarkGray
        $closedStable = $true
        for ($i = 0; $i -lt $StableClosedSeconds; $i++) {
            if ((Get-Date) -ge $deadline) {
                break
            }

            if ((@(Get-RimWorldProcesses)).Count -gt 0) {
                $closedStable = $false
                Write-Host "  RimWorld 在稳定窗口内重新启动，恢复等待关闭流程。" -ForegroundColor Yellow
                break
            }

            Start-Sleep -Seconds 1
        }

        if ($closedStable -and (@(Get-RimWorldProcesses)).Count -eq 0) {
            Write-Host "  RimWorld 已连续关闭 ${StableClosedSeconds}s，继续部署。" -ForegroundColor Green
            return
        }
    }

    Write-Host "等待 RimWorld 关闭超过 ${TimeoutSeconds}s，部署失败。" -ForegroundColor Red
    exit 1
}

Write-Host "======= 开始构建项目: $ModName =======" -ForegroundColor Cyan
Wait-ForRimWorldToClose

# 0.5. 构建 shader bundle
if ($null -ne $ShaderBundleConfig -and $ShaderBundleConfig.enabled) {
    Write-Host ">>> 步骤 0.5: 构建 Shader Bundle..." -ForegroundColor Gray
    powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "shader\build_shader_bundle.ps1")
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Shader bundle 构建失败，请检查 Unity 构建日志！" -ForegroundColor Red
        exit 1
    }
}

# 1. 执行编译
Write-Host ">>> 步骤 1: 编译 C# 项目..." -ForegroundColor Gray
foreach ($csprojPath in $CsprojPaths) {
    if (Test-Path $csprojPath) {
        Write-Host "  -> 编译: $csprojPath" -ForegroundColor DarkGray
        dotnet build "$csprojPath"
        if ($LASTEXITCODE -ne 0) {
            Write-Host "编译失败，请检查代码错误！" -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "未发现 .csproj 文件: $csprojPath" -ForegroundColor Red
        exit 1
    }
}
Write-Host "编译成功！" -ForegroundColor Green

# 2. 清理目标目录
Write-Host "`n>>> 步骤 2: 清理目标目录 (排除保护文件)..." -ForegroundColor Gray
if (Test-Path $modPath) {
    $existingItems = Get-ChildItem -Path $modPath
    foreach ($item in $existingItems) {
        if ($item.Name -notmatch $ProtectedFilesRegex) {
            Remove-Item -Path $item.FullName -Recurse -Force | Out-Null
            Write-Host "  已删除: $($item.Name)" -ForegroundColor DarkGray
        } else {
            Write-Host "  [保护]: $($item.Name)" -ForegroundColor Yellow
        }
    }
} else {
    New-Item -ItemType Directory -Path $modPath -Force | Out-Null
    Write-Host "创建目标目录: $modPath" -ForegroundColor Green
}

# 3. 同步文件
Write-Host "`n>>> 步骤 3: 开始同步文件..." -ForegroundColor Cyan
foreach ($item in $SyncItems) {
    $src = [System.IO.Path]::GetFullPath((Join-Path $devPath $item))
    $dest = Join-Path $modPath $item

    if (Test-Path $src) {
        Write-Host "正在同步: $item" -ForegroundColor Gray

        if (Test-Path $src -PathType Container) {
            # 文件夹同步
            if (-not (Test-Path $dest)) {
                New-Item -ItemType Directory -Path $dest -Force | Out-Null
            }

            if ($item -eq "About") {
                Get-ChildItem -Path $src -File -Recurse | Where-Object {
                    $_.Extension -notin ".svg", ".html"
                } | ForEach-Object {
                    $relativePath = $_.FullName.Substring($src.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
                    $targetPath = Join-Path $dest $relativePath
                    $targetDir = Split-Path $targetPath -Parent
                    if (-not (Test-Path $targetDir)) {
                        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
                    }
                    Copy-Item -LiteralPath $_.FullName -Destination $targetPath -Force
                }
            } else {
                Copy-Item -Path "$src\*" -Destination $dest -Recurse -Force
            }
            
            # 针对 Languages 的特殊显示
            if ($item -eq "Languages") {
                $langDirs = Get-ChildItem -Path $src -Directory
                foreach ($langDir in $langDirs) {
                    $filePaths = Get-ChildItem -Path $langDir.FullName -File -Recurse
                    Write-Host "    $($langDir.Name): $($filePaths.Count) 个文件" -ForegroundColor Green
                }
            }
        } else {
            # 单个文件同步
            $parentDir = Split-Path $dest
            if (-not (Test-Path $parentDir)) {
                New-Item -ItemType Directory -Path $parentDir -Force | Out-Null
            }
            Copy-Item -Path $src -Destination $dest -Force
        }
        Write-Host "  ✓ 完成" -ForegroundColor Green
    } else {
        Write-Host "  ⚠ 跳过: $item (源路径不存在)" -ForegroundColor Yellow
    }
}

Write-Host "`n======= 部署完成！请重启游戏查看效果 =======" -ForegroundColor Cyan
