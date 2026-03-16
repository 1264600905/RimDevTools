# 自动化编译部署脚本,注意在第一次执行部署脚本之前先改名称，别再用TempProject去部署了好嘛？

# ==========================================
# 1. 配置部分 (需要修改的部分写在这里)
# ==========================================

# 模组名称（用于游戏 Mods 文件夹下的子文件夹名）
$ModName = "LogSyncMod"

# 你的 RimWorld Mods 目录路径
$RimWorldModDir = "H:\SteamLibrary\steamapps\common\RimWorld\Mods"

# C# 项目文件路径
$CsprojPath = "$PSScriptRoot\source\TempProject\TempProject.csproj"

# 需要同步到游戏目录的项目 (文件夹或文件)
# 注意：这些路径是相对于项目根目录的，不在列表中的文件（如 .gemini, .cursor, 参考文档等）将不会被部署
$SyncItems = @(
    "1.6\Assemblies",
    "1.6\Defs",
    "1.6\Patches",
    "Languages",
    "About",
    "Textures",
    "MuzzleFlash",
    "CE_Patches",
    "LoadFolders.xml"
)

# 清空目标目录时需要保留的文件或文件夹 (正则表达式)
# PublishedFileId.txt 是 Steam 识别模组的关键，必须保留，否则会导致无法更新模组
$ProtectedFilesRegex = "^(PublishedFileId\.txt)$"

# ==========================================
# 2. 内部逻辑部分 (通常不需要修改)
# ==========================================

$devPath = "$PSScriptRoot"
$modPath = Join-Path $RimWorldModDir $ModName

Write-Host "======= 开始构建项目: $ModName =======" -ForegroundColor Cyan

# 1. 执行编译
Write-Host ">>> 步骤 1: 编译 C# 项目..." -ForegroundColor Gray
if (Test-Path $CsprojPath) {
    dotnet build "$CsprojPath"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "编译失败，请检查代码错误！" -ForegroundColor Red
        exit
    }
    Write-Host "编译成功！" -ForegroundColor Green
} else {
    Write-Host "未发现 .csproj 文件，跳过编译步骤。" -ForegroundColor Yellow
}

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
    $src = Join-Path $devPath $item
    $dest = Join-Path $modPath $item

    if (Test-Path $src) {
        Write-Host "正在同步: $item" -ForegroundColor Gray

        if (Test-Path $src -PathType Container) {
            # 文件夹同步
            if (-not (Test-Path $dest)) {
                New-Item -ItemType Directory -Path $dest -Force | Out-Null
            }
            Copy-Item -Path "$src\*" -Destination $dest -Recurse -Force
            
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
