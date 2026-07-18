param(
    [string]$SvgPath = "About\Preview.svg",
    [string]$OutputPath = "About\Preview.png",
    [int]$Width = 0,
    [int]$Height = 0,
    [int]$DelayMilliseconds = 1500,
    [string]$BrowserPath,
    [int]$PngQuality = 95
)

$ErrorActionPreference = "Stop"
$tempHtmlPath = $null

function New-RenderHtml {
    param(
        [string]$SvgDataUrl,
        [int]$Width,
        [int]$Height
    )

    return @"
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <style>
    html, body {
      margin: 0;
      width: ${Width}px;
      height: ${Height}px;
      overflow: hidden;
      background: #0f1724;
    }

    body {
      background: #0f1724;
      display: flex;
      align-items: stretch;
      justify-content: stretch;
    }

    #canvas,
    #canvas img {
      width: ${Width}px;
      height: ${Height}px;
      display: block;
    }

    #canvas img {
      object-fit: fill;
    }
  </style>
</head>
<body>
  <div id="canvas">
    <img alt="preview" src="${SvgDataUrl}" />
  </div>
</body>
</html>
"@
}

function Resolve-ProjectPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    $projectRoot = Split-Path $PSScriptRoot -Parent
    return [System.IO.Path]::GetFullPath((Join-Path $projectRoot $Path))
}

function Get-BrowserPath {
    param([string]$PreferredPath)

    if ($PreferredPath) {
        $resolvedPreferred = Resolve-ProjectPath $PreferredPath
        if (Test-Path $resolvedPreferred) {
            return $resolvedPreferred
        }

        throw "未找到指定浏览器: $resolvedPreferred"
    }

    $candidates = @(
        "C:\Program Files\Google\Chrome\Application\chrome.exe",
        "C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
        "C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "未找到可用浏览器，请安装 Chrome/Edge 或通过 -BrowserPath 指定路径。"
}

$svgFullPath = Resolve-ProjectPath $SvgPath
$outputFullPath = Resolve-ProjectPath $OutputPath

if (-not (Test-Path $svgFullPath)) {
    throw "未找到 SVG 文件: $svgFullPath"
}

$browserExe = Get-BrowserPath $BrowserPath
$outputDir = Split-Path $outputFullPath -Parent
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

$svgContent = Get-Content -LiteralPath $svgFullPath -Raw
[xml]$svgXml = $svgContent
$svgRoot = $svgXml.DocumentElement

function Get-SvgDimension {
    param(
        [string]$Value,
        [double]$Fallback
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return [int][Math]::Round($Fallback)
    }

    $match = [regex]::Match($Value, '^[\s]*([0-9]+(?:\.[0-9]+)?)')
    if ($match.Success) {
        return [int][Math]::Round([double]$match.Groups[1].Value)
    }

    return [int][Math]::Round($Fallback)
}

$viewBoxParts = @()
if (-not [string]::IsNullOrWhiteSpace($svgRoot.viewBox)) {
    $viewBoxParts = $svgRoot.viewBox -split '[\s,]+' | Where-Object { $_ -ne '' }
}

$fallbackWidth = if ($viewBoxParts.Count -ge 3) { [double]$viewBoxParts[2] } else { 1280 }
$fallbackHeight = if ($viewBoxParts.Count -ge 4) { [double]$viewBoxParts[3] } else { 720 }

if ($Width -le 0) {
    $Width = Get-SvgDimension -Value $svgRoot.width -Fallback $fallbackWidth
}

if ($Height -le 0) {
    $Height = Get-SvgDimension -Value $svgRoot.height -Fallback $fallbackHeight
}

if ($PngQuality -lt 0 -or $PngQuality -gt 100) {
    throw "PngQuality 必须在 0 到 100 之间。"
}

$tempHtmlPath = Join-Path ([System.IO.Path]::GetTempPath()) ("preview-render-{0}.html" -f ([System.Guid]::NewGuid().ToString('N')))
$svgBase64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($svgContent))
$svgDataUrl = "data:image/svg+xml;base64,$svgBase64"
$htmlContent = New-RenderHtml -SvgDataUrl $svgDataUrl -Width $Width -Height $Height
[System.IO.File]::WriteAllText($tempHtmlPath, $htmlContent, [System.Text.Encoding]::UTF8)
$renderUri = [System.Uri]::new($tempHtmlPath).AbsoluteUri
$windowWidth = $Width + 1
$windowHeight = $Height + 95

try {
    $process = Start-Process -FilePath $browserExe -ArgumentList @(
        "--headless=new",
        "--disable-gpu",
        "--hide-scrollbars",
        "--force-device-scale-factor=1",
        "--default-background-color=0f1724",
        "--window-size=${windowWidth},${windowHeight}",
        "--virtual-time-budget=$DelayMilliseconds",
        "--screenshot=$outputFullPath",
        $renderUri
    ) -Wait -NoNewWindow -PassThru

    if ($process.ExitCode -ne 0) {
        throw "浏览器截图失败，退出码: $($process.ExitCode)"
    }
}
finally {
    if ($tempHtmlPath -and (Test-Path $tempHtmlPath)) {
        Remove-Item -LiteralPath $tempHtmlPath -Force -ErrorAction SilentlyContinue
    }
}

Add-Type -AssemblyName System.Drawing
$sourceImage = [System.Drawing.Bitmap]::new($outputFullPath)

if ($sourceImage.Width -ne $Width -or $sourceImage.Height -ne $Height) {
    $cropWidth = [Math]::Min($Width, $sourceImage.Width)
    $cropHeight = [Math]::Min($Height, $sourceImage.Height)
    $croppedImage = [System.Drawing.Bitmap]::new($cropWidth, $cropHeight)

    try {
        $graphics = [System.Drawing.Graphics]::FromImage($croppedImage)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.DrawImage(
                $sourceImage,
                [System.Drawing.Rectangle]::new(0, 0, $cropWidth, $cropHeight),
                [System.Drawing.Rectangle]::new(0, 0, $cropWidth, $cropHeight),
                [System.Drawing.GraphicsUnit]::Pixel
            )
        }
        finally {
            $graphics.Dispose()
        }

        $sourceImage.Dispose()
        $croppedImage.Save($outputFullPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $image = [System.Drawing.Image]::FromFile($outputFullPath)
    }
    finally {
        $croppedImage.Dispose()
    }
}
else {
    $sourceImage.Dispose()
    $image = [System.Drawing.Image]::FromFile($outputFullPath)
}

$finalWidth = $image.Width
$finalHeight = $image.Height
$image.Dispose()

$pngEncoder = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() |
    Where-Object { $_.MimeType -eq 'image/png' } |
    Select-Object -First 1

if ($null -ne $pngEncoder) {
    $tempOptimizedPath = Join-Path ([System.IO.Path]::GetTempPath()) ("preview-optimized-{0}.png" -f ([System.Guid]::NewGuid().ToString('N')))
    try {
        $encoderParameters = [System.Drawing.Imaging.EncoderParameters]::new(1)
        $encoderParameters.Param[0] = [System.Drawing.Imaging.EncoderParameter]::new([System.Drawing.Imaging.Encoder]::Quality, [long]$PngQuality)

        $optimizedImage = [System.Drawing.Image]::FromFile($outputFullPath)
        try {
            $optimizedImage.Save($tempOptimizedPath, $pngEncoder, $encoderParameters)
        }
        finally {
            $optimizedImage.Dispose()
            $encoderParameters.Dispose()
        }

        if ((Test-Path $tempOptimizedPath) -and ((Get-Item $tempOptimizedPath).Length -lt (Get-Item $outputFullPath).Length)) {
            Remove-Item -LiteralPath $outputFullPath -Force
            Move-Item -LiteralPath $tempOptimizedPath -Destination $outputFullPath
        }
    }
    finally {
        if (Test-Path $tempOptimizedPath) {
            Remove-Item -LiteralPath $tempOptimizedPath -Force -ErrorAction SilentlyContinue
        }
    }
}

$fileSize = (Get-Item $outputFullPath).Length
Write-Host "Preview 导出完成: $outputFullPath" -ForegroundColor Green
Write-Host "尺寸: ${finalWidth}x${finalHeight}" -ForegroundColor Cyan
Write-Host "文件大小: $fileSize bytes" -ForegroundColor Cyan
