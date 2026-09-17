param(
    [Parameter(Mandatory = $true)][string]$SourceImage
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $root 'Bloxstrap'
$sizes = 16, 24, 32, 48, 64, 128, 256

$source = [System.Drawing.Image]::FromFile($SourceImage)

function New-Resized-Bitmap([System.Drawing.Image]$source, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $srcW = [float]$source.Width
    $srcH = [float]$source.Height
    $scale = [Math]::Min($size / $srcW, $size / $srcH)
    $dstW = [int][Math]::Floor($srcW * $scale)
    $dstH = [int][Math]::Floor($srcH * $scale)
    $x = [int][Math]::Floor(($size - $dstW) / 2)
    $y = [int][Math]::Floor(($size - $dstH) / 2)
    $g.DrawImage($source, $x, $y, $dstW, $dstH)
    $g.Dispose()
    return $bmp
}

function Get-BgraBytes([System.Drawing.Bitmap]$bmp) {
    $rect = New-Object System.Drawing.Rectangle(0, 0, $bmp.Width, $bmp.Height)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $data.Stride
        $bytes = New-Object byte[] ($stride * $bmp.Height)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)

        $flipped = New-Object byte[] $bytes.Length
        for ($row = 0; $row -lt $bmp.Height; $row++) {
            [Array]::Copy($bytes, $row * $stride, $flipped, ($bmp.Height - 1 - $row) * $stride, $stride)
        }

        return $flipped
    } finally {
        $bmp.UnlockBits($data)
    }
}

function New-Ico([System.Drawing.Image]$source, [int[]]$iconSizes) {
    $count = $iconSizes.Count
    $header = [byte[]]@(0, 0, 1, 0, [byte]$count, [byte]($count -shr 8))
    $entries = New-Object System.Collections.Generic.List[byte[]]
    $imageBlobs = New-Object System.Collections.Generic.List[byte[]]
    $offset = 6 + 16 * $count

    foreach ($size in $iconSizes) {
        $bmp = New-Resized-Bitmap $source $size
        $bgra = Get-BgraBytes $bmp
        $bmp.Dispose()

        $xorSize = $size * $size * 4
        $maskRow = [int][Math]::Ceiling($size / 8.0)
        $maskRowPadded = [int]([Math]::Ceiling($maskRow / 4.0) * 4)
        $andSize = $maskRowPadded * $size

        $bmpInfo = New-Object System.Collections.Generic.List[byte]
        $bmpInfo.AddRange([byte[]]@(40, 0, 0, 0))                                   # biSize
        $bmpInfo.AddRange([BitConverter]::GetBytes([int32]$size))                    # biWidth
        $bmpInfo.AddRange([BitConverter]::GetBytes([int32]($size * 2)))              # biHeight (XOR + AND)
        $bmpInfo.AddRange([BitConverter]::GetBytes([uint16]1))                       # biPlanes
        $bmpInfo.AddRange([BitConverter]::GetBytes([uint16]32))                      # biBitCount
        $bmpInfo.AddRange([byte[]]@(0, 0, 0, 0))                                     # biCompression
        $bmpInfo.AddRange([BitConverter]::GetBytes([uint32]($xorSize + $andSize)))   # biSizeImage
        $bmpInfo.AddRange([byte[]]@(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0))             # biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant
        $bmpInfoBytes = $bmpInfo.ToArray()

        $andMask = New-Object byte[] $andSize
        $imageBlob = $bmpInfoBytes + $bgra + $andMask

        $w = if ($size -eq 256) { 0 } else { $size }
        $entry = [byte[]]@($w, $w, 0, 0) +
            [BitConverter]::GetBytes([uint16]1) +
            [BitConverter]::GetBytes([uint16]32) +
            [BitConverter]::GetBytes([uint32]$imageBlob.Length) +
            [BitConverter]::GetBytes([uint32]$offset)

        $entries.Add($entry)
        $imageBlobs.Add($imageBlob)
        $offset += $imageBlob.Length
    }

    $bytes = $header
    foreach ($e in $entries) { $bytes += $e }
    foreach ($i in $imageBlobs) { $bytes += $i }
    return $bytes
}

$icoBytes = New-Ico $source $sizes
$pngBmp = New-Resized-Bitmap $source 256

[System.IO.File]::WriteAllBytes((Join-Path $projectDir 'Bloxstrap.ico'), $icoBytes)
[System.IO.File]::WriteAllBytes((Join-Path $projectDir 'Resources\IconCatstrap.ico'), $icoBytes)
[System.IO.File]::WriteAllBytes((Join-Path $projectDir 'Resources\IconBloxstrap.ico'), $icoBytes)
$pngBmp.Save((Join-Path $projectDir 'Resources\IconCatstrap.png'), [System.Drawing.Imaging.ImageFormat]::Png)

$pngBmp.Dispose()
$source.Dispose()

$test = New-Object System.Drawing.Icon((Join-Path $projectDir 'Bloxstrap.ico'))
Write-Host "OK: generated $($icoBytes.Length) byte ico with $($sizes.Count) sizes; icon loads: $($test.Width)x$($test.Height)"
$test.Dispose()