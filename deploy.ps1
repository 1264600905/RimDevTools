# 边缘赤潮 自动化编译部署脚本

$devPath = "$PSScriptRoot"
$modPath = "H:\SteamLibrary\steamapps\common\RimWorld\Mods\LogSyncMod"

Write-Host "======= 开始构建项目 =======" -ForegroundColor Cyan

# 1. 执行编译
dotnet build "$devPath\source\TempProject\TempProject.csproj"

if ($LASTEXITCODE -ne 0) {
    Write-Host "编译失败，请检查代码错误！" -ForegroundColor Red
    exit
}

Write-Host "编译成功，开始同步文件..." -ForegroundColor Green

# 2. 定义需要同步的项目列表（文件夹或文件）
$items = @(
    "1.6\Assemblies",
    "1.6\Defs",
    "1.6\Patches",
    "Languages",
    "About",
    "Textures",
    "MuzzleFlash",
    "CE_Patches",
    "Show_Me_Your_Hand",
    "WeaponTweakData",
    "LoadFolders.xml"
)

# 3. 循环同步
foreach ($item in $items) {
    $src = Join-Path $devPath $item
    $dest = Join-Path $modPath $item

    if (Test-Path $src) {
        Write-Host "正在同步: $item" -ForegroundColor Gray

        if (Test-Path $src -PathType Container) {
            # 文件夹同步逻辑
            if (-not (Test-Path $dest)) {
                New-Item -ItemType Directory -Path $dest -Force | Out-Null
                Write-Host "  创建目录: $dest" -ForegroundColor Yellow
            }
            Copy-Item -Path "$src\*" -Destination $dest -Recurse -Force

            # 如果是Languages目录，额外显示语言信息
            if ($item -eq "Languages") {
                Write-Host "  语言文件详情:" -ForegroundColor Cyan
                $langDirs = Get-ChildItem -Path $src -Directory
                foreach ($langDir in $langDirs) {
                    $fileCount = (Get-ChildItem -Path $langDir.FullName -File -Recurse).Count
                    Write-Host "    $($langDir.Name): $fileCount 个文件" -ForegroundColor Green
                }
            }
        } else {
            # 单个文件同步逻辑
            $parentDir = Split-Path $dest
            if (-not (Test-Path $parentDir)) {
                New-Item -ItemType Directory -Path $parentDir -Force | Out-Null
                Write-Host "  创建目录: $parentDir" -ForegroundColor Yellow
            }
            Copy-Item -Path $src -Destination $dest -Force
        }

        Write-Host "  ✓ 完成: $item" -ForegroundColor Green
    } else {
        Write-Host "  ⚠ 跳过: $item (源路径不存在)" -ForegroundColor Yellow
    }
}

# 4. 验证语言文件
Write-Host "======= 验证语言文件 =======" -ForegroundColor Cyan
$langPath = Join-Path $modPath "Languages"
if (Test-Path $langPath) {
    Write-Host "语言文件已部署到: $langPath" -ForegroundColor Green
    $languages = @("ChineseSimplified", "ChineseTraditional", "English")
    foreach ($lang in $languages) {
        $langFolder = Join-Path $langPath $lang
        if (Test-Path $langFolder) {
            $fileCount = (Get-ChildItem -Path $langFolder -File -Recurse).Count
            Write-Host "  $lang : $fileCount 个本地化文件" -ForegroundColor Green
        } else {
            Write-Host "  $lang : 目录不存在" -ForegroundColor Red
        }
    }
} else {
    Write-Host "警告: Languages目录未正确部署！" -ForegroundColor Red
}

Write-Host "======= 部署完成！请重启游戏查看效果 =======" -ForegroundColor Cyan
Write-Host "提示: 在游戏设置中可以切换语言来测试本地化效果" -ForegroundColor Yellow
