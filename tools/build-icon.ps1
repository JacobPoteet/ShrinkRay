<#
.SYNOPSIS
  Rebuilds the ShrinkRay icons from the SVG sources in src/ShrinkRay.App/Assets/Icon.

.DESCRIPTION
  Renders each hand-tuned cassette-<size>.svg through WPF (no outside tools) and writes:
    ShrinkRay.ico        16, 20, 24, 32, 48 and 256 px, for the exe, taskbar and title bar
    tray-idle.ico        16, 20, 24 and 32 px, the plain cassette
    tray-encoding.ico    the window lit in amber phosphor
    tray-paused.ico      the whole cassette dimmed
    tray-failed.ico      the cassette with the failed-episode mark (not red)

  The SVG files may only use <rect> (rx), <circle> and <path> with solid fill and optional
  stroke. Elements named id="window", class="reel" and class="hub" are recoloured for the
  encoding state; mark-<size>.svg is drawn on top for the failed state.

  Run from PowerShell 5.1:  powershell -ExecutionPolicy Bypass -File tools/build-icon.ps1
  -Preview <folder> also writes every frame as a PNG, plus 8x nearest-neighbour sheets of the
  small frames on a light (#F3F3F3) and a dark (#202020) taskbar.
#>
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\src\ShrinkRay.App\Assets\Icon'),
    [string]$OutDir = (Join-Path $PSScriptRoot '..\src\ShrinkRay.App\Assets\Icon'),
    [string]$Preview = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

$Source = (Resolve-Path $Source).Path
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }
$OutDir = (Resolve-Path $OutDir).Path

$culture = [Globalization.CultureInfo]::InvariantCulture
function Num($text, $default = 0.0) {
    if ($null -eq $text -or $text -eq '') { return [double]$default }
    return [double]::Parse($text, $culture)
}

function Brush($color) {
    if (-not $color -or $color -eq 'none') { return $null }
    $b = New-Object Windows.Media.SolidColorBrush ([Windows.Media.ColorConverter]::ConvertFromString($color))
    $b.Freeze()
    return $b
}

# State recolouring, keyed by what an element is called in the SVG.
function StateFill($element, $state) {
    $fill = $element.GetAttribute('fill')
    if ($state -ne 'encoding') { return $fill }
    if ($element.GetAttribute('id') -eq 'window') { return '#F2B04A' }
    switch ($element.GetAttribute('class')) {
        'reel' { return '#26221C' }
        'hub' { return '#F2B04A' }
    }
    return $fill
}

function DrawSvg($dc, $path, $state) {
    [xml]$svg = Get-Content -LiteralPath $path -Raw
    foreach ($e in $svg.SelectNodes("//*[local-name()='rect' or local-name()='circle' or local-name()='path']")) {
        $brush = Brush (StateFill $e $state)
        $pen = $null
        if ($e.GetAttribute('stroke')) {
            $pen = New-Object Windows.Media.Pen (Brush $e.GetAttribute('stroke')), (Num $e.GetAttribute('stroke-width') 1)
            $pen.Freeze()
        }
        switch ($e.LocalName) {
            'rect' {
                $r = New-Object Windows.Rect (Num $e.GetAttribute('x')), (Num $e.GetAttribute('y')), (Num $e.GetAttribute('width')), (Num $e.GetAttribute('height'))
                $rx = Num $e.GetAttribute('rx')
                $dc.DrawRoundedRectangle($brush, $pen, $r, $rx, $rx)
            }
            'circle' {
                $c = New-Object Windows.Point (Num $e.GetAttribute('cx')), (Num $e.GetAttribute('cy'))
                $dc.DrawEllipse($brush, $pen, $c, (Num $e.GetAttribute('r')), (Num $e.GetAttribute('r')))
            }
            'path' {
                $dc.DrawGeometry($brush, $pen, [Windows.Media.Geometry]::Parse($e.GetAttribute('d')))
            }
        }
    }
}

function Render([int]$size, [string]$state) {
    $visual = New-Object Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    if ($state -eq 'paused') { $dc.PushOpacity(0.45) }
    DrawSvg $dc (Join-Path $Source "cassette-$size.svg") $state
    if ($state -eq 'paused') { $dc.Pop() }
    if ($state -eq 'failed') { DrawSvg $dc (Join-Path $Source "mark-$size.svg") 'failed' }
    $dc.Close()
    $bmp = New-Object Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($visual)
    $bmp.Freeze()
    return $bmp
}

function PngBytes($bitmap) {
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $ms = New-Object IO.MemoryStream
    $encoder.Save($ms)
    return $ms.ToArray()
}

# A 32-bit DIB frame as ICO stores it: bottom-up BGRA, then a 1-bit AND mask (all zero).
function DibBytes($bitmap, [int]$size) {
    $stride = $size * 4
    $px = New-Object byte[] ($stride * $size)
    $bitmap.CopyPixels($px, $stride, 0)
    $ms = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $ms
    $w.Write([int]40); $w.Write([int]$size); $w.Write([int]($size * 2))
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]0)
    $w.Write([int]($stride * $size)); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $i = $y * $stride + $x * 4
            $a = $px[$i + 3]
            # Pbgra32 is premultiplied; ICO wants straight alpha.
            foreach ($c in 0..2) {
                $v = $px[$i + $c]
                if ($a -gt 0 -and $a -lt 255) { $v = [math]::Min(255, [int][math]::Round($v * 255.0 / $a)) }
                $w.Write([byte]$v)
            }
            $w.Write([byte]$a)
        }
    }
    $maskRow = [int]([math]::Ceiling($size / 32.0) * 4)
    $w.Write((New-Object byte[] ($maskRow * $size)))
    $w.Flush()
    return $ms.ToArray()
}

function WriteIco([string]$file, [int[]]$sizes, [string]$state) {
    $frames = foreach ($s in $sizes) {
        $bmp = Render $s $state
        $data = if ($s -ge 256) { PngBytes $bmp } else { DibBytes $bmp $s }
        [pscustomobject]@{ Size = $s; Data = [byte[]]$data }
    }
    $ms = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $ms
    $w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]@($frames).Count)
    $offset = 6 + 16 * @($frames).Count
    foreach ($f in $frames) {
        $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([int16]1); $w.Write([int16]32)
        $w.Write([int]$f.Data.Length); $w.Write([int]$offset)
        $offset += $f.Data.Length
    }
    foreach ($f in $frames) { $w.Write($f.Data) }
    $w.Flush()
    [IO.File]::WriteAllBytes($file, $ms.ToArray())
    Write-Host ("{0}: {1}" -f (Split-Path $file -Leaf), ($sizes -join ', '))
}

# Nearest-neighbour enlargement by whole pixels, so a preview shows exactly what the frame holds.
function Enlarge($bitmap, [int]$size, [int]$factor) {
    $stride = $size * 4
    $src = New-Object byte[] ($stride * $size)
    $bitmap.CopyPixels($src, $stride, 0)
    $big = $size * $factor
    $dst = New-Object byte[] ($big * 4 * $big)
    for ($y = 0; $y -lt $big; $y++) {
        for ($x = 0; $x -lt $big; $x++) {
            [Array]::Copy($src, ([int][math]::Floor($y / $factor)) * $stride + ([int][math]::Floor($x / $factor)) * 4, $dst, $y * $big * 4 + $x * 4, 4)
        }
    }
    return [Windows.Media.Imaging.BitmapSource]::Create($big, $big, 96, 96, [Windows.Media.PixelFormats]::Pbgra32, $null, $dst, $big * 4)
}

function SavePng($bitmap, [string]$file) { [IO.File]::WriteAllBytes($file, (PngBytes $bitmap)) }

WriteIco (Join-Path $OutDir 'ShrinkRay.ico') @(16, 20, 24, 32, 48, 256) 'normal'
$traySizes = @(16, 20, 24, 32)
WriteIco (Join-Path $OutDir 'tray-idle.ico') $traySizes 'normal'
WriteIco (Join-Path $OutDir 'tray-encoding.ico') $traySizes 'encoding'
WriteIco (Join-Path $OutDir 'tray-paused.ico') $traySizes 'paused'
WriteIco (Join-Path $OutDir 'tray-failed.ico') $traySizes 'failed'

if ($Preview) {
    New-Item -ItemType Directory -Force -Path $Preview | Out-Null
    $Preview = (Resolve-Path $Preview).Path
    $states = 'normal', 'encoding', 'paused', 'failed'
    foreach ($s in 16, 20, 24, 32, 48, 256) {
        SavePng (Render $s 'normal') (Join-Path $Preview "frame-$s.png")
    }
    $zoom = 8
    foreach ($s in 16, 20, 24) {
        $cell = $s * $zoom
        $pad = 16
        $width = ($cell + $pad) * $states.Count + $pad
        $height = ($cell + $pad) * 2 + $pad
        $visual = New-Object Windows.Media.DrawingVisual
        [Windows.Media.RenderOptions]::SetBitmapScalingMode($visual, [Windows.Media.BitmapScalingMode]::NearestNeighbor)
        $dc = $visual.RenderOpen()
        $rows = @(@{ Bg = '#F3F3F3'; Y = 0 }, @{ Bg = '#202020'; Y = 1 })
        foreach ($row in $rows) {
            $top = $pad + $row.Y * ($cell + $pad)
            $bandTop = if ($row.Y -eq 0) { 0 } else { $top - $pad / 2 }
            $bandHeight = if ($row.Y -eq 0) { $top + $cell + $pad / 2 } else { $height - $bandTop }
            $dc.DrawRectangle((Brush $row.Bg), $null, (New-Object Windows.Rect 0, $bandTop, $width, $bandHeight))
            for ($i = 0; $i -lt $states.Count; $i++) {
                $bmp = Enlarge (Render $s $states[$i]) $s $zoom
                $dc.DrawImage($bmp, (New-Object Windows.Rect ($pad + $i * ($cell + $pad)), $top, $cell, $cell))
            }
        }
        $dc.Close()
        $sheet = New-Object Windows.Media.Imaging.RenderTargetBitmap $width, $height, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
        $sheet.Render($visual)
        SavePng $sheet (Join-Path $Preview "sheet-$s-8x.png")
    }
    Write-Host "Preview PNGs in $Preview"
}
