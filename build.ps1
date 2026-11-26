$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$src = Join-Path $root 'src'
$out = Join-Path $root 'AntigravityAccountManager.exe'
$icon = Join-Path $root 'app.ico'
$manifest = Join-Path $root 'app.manifest'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $csc)) {
    throw "找不到 csc.exe：$csc"
}

if (-not (Test-Path -LiteralPath $icon)) {
    Add-Type -AssemblyName System.Drawing
    $bitmap = New-Object System.Drawing.Bitmap 64, 64
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $rect = New-Object System.Drawing.Rectangle 0, 0, 64, 64
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect,
        ([System.Drawing.Color]::FromArgb(38, 27, 76)),
        ([System.Drawing.Color]::FromArgb(108, 76, 241)), 45.0
    $graphics.FillRectangle($brush, $rect)
    $font = New-Object System.Drawing.Font 'Segoe UI', 34, ([System.Drawing.FontStyle]::Bold)
    $textBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $rectF = New-Object System.Drawing.RectangleF 0, 0, 64, 64
    $graphics.DrawString('A', $font, $textBrush, $rectF, $format)
    $png = New-Object System.IO.MemoryStream
    $bitmap.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
    $data = $png.ToArray()
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter $stream
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]1)
    $writer.Write([Byte]64)
    $writer.Write([Byte]64)
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$data.Length)
    $writer.Write([UInt32]22)
    $writer.Write($data)
    [System.IO.File]::WriteAllBytes($icon, $stream.ToArray())
    $writer.Dispose()
    $stream.Dispose()
    $png.Dispose()
    $textBrush.Dispose()
    $font.Dispose()
    $brush.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

$sources = Get-ChildItem -LiteralPath $src -Filter '*.cs' | ForEach-Object { $_.FullName }
$references = @(
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Web.Extensions.dll',
    'System.Net.Http.dll'
)

$responseFile = Join-Path $root 'build.rsp'
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('/nologo')
$lines.Add('/target:winexe')
$lines.Add('/platform:anycpu')
$lines.Add('/optimize+')
$lines.Add('/codepage:65001')
$lines.Add('/win32icon:"' + $icon + '"')
$lines.Add('/win32manifest:"' + $manifest + '"')
$lines.Add('/out:"' + $out + '"')
$lines.Add('/reference:' + ($references -join ','))
foreach ($source in $sources) { $lines.Add('"' + $source + '"') }
[System.IO.File]::WriteAllLines($responseFile, $lines.ToArray(),
    [System.Text.Encoding]::Default)
& $csc ('@' + $responseFile)
Remove-Item -LiteralPath $responseFile -Force -ErrorAction SilentlyContinue

if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }
Write-Host "已生成：$out" -ForegroundColor Cyan
