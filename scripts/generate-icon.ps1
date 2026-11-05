# Generates Assets/PasteHatch.ico (multi-size, uncompressed 32-bit DIB entries) so the app,
# taskbar and notification-area icon share one source. Run from the repository root:
#   powershell -ExecutionPolicy Bypass -File scripts/generate-icon.ps1
param(
    [string]$OutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\PasteHatch.App\Assets\PasteHatch.ico'),
    [string]$PreviewPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'docs\images\icon.png')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

function New-RoundedRectPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Get-IconBitmap([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $inset = [Math]::Max(2.0, $size * 0.06)
    $background = New-RoundedRectPath $inset $inset ($size - 2 * $inset) ($size - 2 * $inset) ([Math]::Max(3.0, $size * 0.22))
    $backgroundBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 15, 108, 189))
    $graphics.FillPath($backgroundBrush, $background)
    $backgroundBrush.Dispose()
    $background.Dispose()

    # Design on a 16px grid: a book and a storage box stand on one shelf.
    # Solid shapes and open gaps retain the silhouette in the notification area.
    $unit = $size / 16.0
    $itemBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    try {
        $graphics.FillRectangle($itemBrush, [float](4 * $unit), [float](3 * $unit), [float](2 * $unit), [float](7 * $unit))
        $graphics.FillRectangle($itemBrush, [float](7 * $unit), [float](6 * $unit), [float](5 * $unit), [float](4 * $unit))
        $handleBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 15, 108, 189))
        try {
            $graphics.FillRectangle($handleBrush, [float](9 * $unit), [float](7 * $unit), [float]$unit, [float]$unit)
        }
        finally {
            $handleBrush.Dispose()
        }
        $shelf = New-RoundedRectPath (3 * $unit) (10 * $unit) (10 * $unit) (2 * $unit) (0.4 * $unit)
        $graphics.FillPath($itemBrush, $shelf)
        $shelf.Dispose()
        $graphics.FillRectangle($itemBrush, [float](4 * $unit), [float](12 * $unit), [float]$unit, [float]$unit)
        $graphics.FillRectangle($itemBrush, [float](11 * $unit), [float](12 * $unit), [float]$unit, [float]$unit)
    }
    finally {
        $itemBrush.Dispose()
    }

    $graphics.Dispose()
    return $bitmap
}

# Returns the DIB representation System.Drawing (and the Win32 resource compiler) expect:
# BITMAPINFOHEADER + bottom-up BGRA XOR bitmap + 1bpp AND mask.
function Get-IconDibBytes([System.Drawing.Bitmap]$bitmap) {
    $size = $bitmap.Width
    $height = $bitmap.Height
    $xorStride = $size * 4
    $andStride = [int]([Math]::Ceiling($size / 32.0) * 4)
    $andSize = $andStride * $height

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    try {
        # BITMAPINFOHEADER
        $writer.Write([UInt32]40)
        $writer.Write([Int32]$size)
        $writer.Write([Int32]($height * 2)) # XOR + AND
        $writer.Write([UInt16]1)            # planes
        $writer.Write([UInt16]32)           # bpp
        $writer.Write([UInt32]0)            # BI_RGB
        $writer.Write([UInt32]($xorStride * $height + $andSize))
        $writer.Write([Int32]0)
        $writer.Write([Int32]0)
        $writer.Write([UInt32]0)
        $writer.Write([UInt32]0)

        $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $height)
        $data = $bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $buffer = New-Object byte[] ($data.Stride * $height)
            [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buffer, 0, $buffer.Length)
            $rowBytes = New-Object byte[] $xorStride
            for ($row = 0; $row -lt $height; $row++) {
                $sourceRow = $height - 1 - $row # DIB rows are bottom-up
                [Array]::Copy($buffer, $sourceRow * $data.Stride, $rowBytes, 0, $xorStride)
                $writer.Write($rowBytes)
            }
        }
        finally {
            $bitmap.UnlockBits($data)
        }

        # AND mask stays all zeros: the 32-bit alpha channel carries transparency.
        $writer.Write((New-Object byte[] $andSize))
        $writer.Flush()
        return $stream.ToArray()
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

$sizeList = New-Object 'System.Collections.Generic.List[int]'
$dibList = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($size in $sizes) {
    $bitmap = Get-IconBitmap $size
    try {
        $dib = [byte[]](Get-IconDibBytes $bitmap)
        $sizeList.Add($size)
        $dibList.Add($dib)
    }
    finally {
        $bitmap.Dispose()
    }
}

$outputDir = Split-Path -Parent $OutputPath
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
}

$file = [System.IO.File]::Create($OutputPath)
$writer = New-Object System.IO.BinaryWriter($file)
try {
    $writer.Write([UInt16]0)             # reserved
    $writer.Write([UInt16]1)             # type: icon
    $writer.Write([UInt16]$sizeList.Count) # image count

    $offset = 6 + 16 * $sizeList.Count
    for ($i = 0; $i -lt $sizeList.Count; $i++) {
        $size = $sizeList[$i]
        $dib = $dibList[$i]
        $dimension = if ($size -ge 256) { 0 } else { $size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)    # palette count
        $writer.Write([byte]0)    # reserved
        $writer.Write([UInt16]1)  # color planes
        $writer.Write([UInt16]32) # bits per pixel
        $writer.Write([UInt32]$dib.Length)
        $writer.Write([UInt32]$offset)
        $offset += $dib.Length
    }

    for ($i = 0; $i -lt $sizeList.Count; $i++) {
        $writer.Write($dibList[$i])
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

Write-Host "Wrote $OutputPath ($((Get-Item $OutputPath).Length) bytes, $($sizeList.Count) sizes)"

# Keep the documentation artwork generated from the very same drawing as the ICO.
$previewDir = Split-Path -Parent $PreviewPath
if (-not (Test-Path $previewDir)) {
    New-Item -ItemType Directory -Force -Path $previewDir | Out-Null
}
$preview = Get-IconBitmap 256
try {
    $preview.Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $preview.Dispose()
}
Write-Host "Wrote $PreviewPath (256px transparent PNG)"
