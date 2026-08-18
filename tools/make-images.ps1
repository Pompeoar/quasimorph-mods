<#
.SYNOPSIS
    Builds the Steam Workshop preview image and gallery images for PerkCooldownHud.

.DESCRIPTION
    Writes:
      src\PerkCooldownHud\thumbnail.png            the grid tile; mod_updateworkshopitem
                                                   uploads exactly this filename
      src\PerkCooldownHud\workshop-images\*.png    gallery shots, added by hand on the item
                                                   page - no console command uploads these

    The source captures are 5120x1440 ultrawide, where the effect bar is a ~900px sliver in
    a mostly black frame. Everything here is a crop of that bar, scaled nearest-neighbour
    because the game is pixel art and bilinear smears it.

    One trap worth naming: the mod's signal is a yellow BORDER on a dimmed panel, while the
    game independently uses a yellow ICON FILL for the damage shield, which is vanilla and
    unrelated. Both read as "yellow panel" at a glance, and an earlier version of this
    script shipped a thumbnail showcasing two damage shields. The zoom rectangle below is
    pinned by coordinate to the 12 / 12 / 1 run of genuinely cooling perks.
#>
[CmdletBinding()]
param(
    [string]$SourceDir = 'C:\Users\pompe\Downloads\Examples',
    [string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\PerkCooldownHud')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# COOLING: four perks mid-cooldown (12, 12, 1 and 4 ticks left) - yellow border, dimmed.
# NORMAL : nothing on cooldown; every panel is stock teal.
$cooling = Join-Path $SourceDir '20260818162154_1.jpg'
$normal  = Join-Path $SourceDir '20260818161922_1.jpg'

foreach ($f in @($cooling, $normal)) {
    if (-not (Test-Path $f)) { throw "Source screenshot not found: $f" }
}

# Regions measured from the 5120x1440 captures.
$coolingBar  = New-Object System.Drawing.Rectangle 2130, 35, 900, 110
$normalBar   = New-Object System.Drawing.Rectangle 2100, 35, 1010, 110
$coolingZoom = New-Object System.Drawing.Rectangle 2244, 50, 274, 86   # the 12 / 12 / 1 run

$bg     = [System.Drawing.ColorTranslator]::FromHtml('#14120F')
$yellow = [System.Drawing.ColorTranslator]::FromHtml('#E9D24B')
$muted  = [System.Drawing.ColorTranslator]::FromHtml('#9AA79A')

function Save-Png($bmp, [string]$path) {
    $dir = Split-Path -Parent $path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $len = (Get-Item $path).Length
    Write-Host ("  {0,-42} {1,9:N0} bytes" -f (Split-Path -Leaf $path), $len) -ForegroundColor Green
    if ($len -gt 1MB) { Write-Warning "$path exceeds Steam's 1 MB preview limit." }
}

function New-Graphics($bmp) {
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    return $g
}

# A straight crop of the effect bar, scaled up. No overlay - the bar speaks for itself.
function Write-Bar($img, $srcRect, [double]$scale, [string]$path) {
    $w = [int]($srcRect.Width * $scale)
    $h = [int]($srcRect.Height * $scale)
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = New-Graphics $bmp
    $g.DrawImage($img, (New-Object System.Drawing.Rectangle 0, 0, $w, $h), $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    Save-Png $bmp $path
    $bmp.Dispose()
}

function Add-Text($g, [string]$text, [int]$cx, [int]$cy, [int]$size, $color, [string]$style = 'Bold') {
    $font = New-Object System.Drawing.Font 'Consolas', $size, ([System.Drawing.FontStyle]::$style)
    $brush = New-Object System.Drawing.SolidBrush $color
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = [System.Drawing.StringAlignment]::Center
    $g.DrawString($text, $font, $brush, $cx, $cy, $fmt)
    $font.Dispose(); $brush.Dispose()
}

$imgCooling = [System.Drawing.Image]::FromFile($cooling)
$imgNormal  = [System.Drawing.Image]::FromFile($normal)
$galleryDir = Join-Path $OutDir 'workshop-images'

try {
    Write-Host 'Writing images:' -ForegroundColor Cyan

    # --- gallery: the two bars, exactly as framed -----------------------------------------
    Write-Bar $imgCooling $coolingBar 1.8 (Join-Path $galleryDir '01-cooling.png')
    Write-Bar $imgNormal  $normalBar  1.6 (Join-Path $galleryDir '02-normal.png')

    # --- thumbnail: 512x512 grid tile -----------------------------------------------------
    # A 1620x198 strip is unreadable as a square tile, so the tile is composed: the whole
    # bar for context, then a zoom on three panels that are actually cooling down.
    $bmp = New-Object System.Drawing.Bitmap 512, 512
    $g = New-Graphics $bmp
    $g.Clear($bg)

    Add-Text $g 'PERK COOLDOWN' 256 22 27 $yellow
    Add-Text $g 'HUD' 256 56 27 $yellow

    $barW = 480
    $barH = [int]($coolingBar.Height * ($barW / $coolingBar.Width))
    $g.DrawImage($imgCooling, (New-Object System.Drawing.Rectangle 16, 118, $barW, $barH), $coolingBar, [System.Drawing.GraphicsUnit]::Pixel)

    $zoomW = 380
    $zoomH = [int]($coolingZoom.Height * ($zoomW / $coolingZoom.Width))
    $zoomRect = New-Object System.Drawing.Rectangle 66, 232, $zoomW, $zoomH
    $g.DrawImage($imgCooling, $zoomRect, $coolingZoom, [System.Drawing.GraphicsUnit]::Pixel)

    $pen = New-Object System.Drawing.Pen $yellow, 2
    $g.DrawRectangle($pen, $zoomRect)
    $pen.Dispose()

    Add-Text $g 'YELLOW BORDER = COOLING DOWN' 256 384 14 $yellow
    Add-Text $g 'NUMBER = TICKS UNTIL READY' 256 414 13 $muted 'Regular'
    Add-Text $g 'vanilla just hides the icon' 256 440 13 $muted 'Regular'

    $g.Dispose()
    Save-Png $bmp (Join-Path $OutDir 'thumbnail.png')
    $bmp.Dispose()
}
finally {
    $imgCooling.Dispose()
    $imgNormal.Dispose()
}

Write-Host ''
Write-Host 'thumbnail.png is uploaded by mod_updateworkshopitem.' -ForegroundColor Yellow
Write-Host 'workshop-images\*.png must be added by hand on the Workshop item page.' -ForegroundColor Yellow
