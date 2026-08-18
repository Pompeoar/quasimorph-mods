<#
.SYNOPSIS
    Builds the Steam Workshop preview image and gallery images for PerkCooldownHud.

.DESCRIPTION
    Writes:
      src\PerkCooldownHud\thumbnail.png            the grid tile; mod_updateworkshopitem
                                                   uploads exactly this filename
      src\PerkCooldownHud\workshop-images\*        gallery shots, added by hand on the item
                                                   page - no console command uploads these

    The gallery is a before/after pair twice over: first cropped to the effect bar so the
    feature is actually legible, then the untouched full-screen captures, because Workshop
    subscribers reasonably distrust a listing that only shows zoomed-in fragments. The
    full-screen pair is copied byte-for-byte rather than re-encoded, so it stays a genuine
    unedited screenshot.

    The source captures are 5120x1440 ultrawide, where the effect bar is a ~900px sliver in
    a mostly black frame. The cropped pair is scaled nearest-neighbour because the game is
    pixel art and bilinear smears it.

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

# BEFORE: nothing on cooldown; every panel is stock teal.
# AFTER : four perks mid-cooldown (12, 12, 1 and 4 ticks left) - yellow border, dimmed.
$before = Join-Path $SourceDir '20260818161922_1.jpg'
$after  = Join-Path $SourceDir '20260818162154_1.jpg'

foreach ($f in @($before, $after)) {
    if (-not (Test-Path $f)) { throw "Source screenshot not found: $f" }
}

# Regions measured from the 5120x1440 captures.
$beforeBar   = New-Object System.Drawing.Rectangle 2100, 35, 1010, 110
$afterBar    = New-Object System.Drawing.Rectangle 2130, 35, 900, 110
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

$imgBefore = [System.Drawing.Image]::FromFile($before)
$imgAfter  = [System.Drawing.Image]::FromFile($after)
$galleryDir = Join-Path $OutDir 'workshop-images'

try {
    if (Test-Path $galleryDir) { Remove-Item (Join-Path $galleryDir '*') -Force }

    Write-Host 'Writing images:' -ForegroundColor Cyan

    # --- gallery 1-2: the effect bar, cropped, before and after ---------------------------
    Write-Bar $imgBefore $beforeBar 1.6 (Join-Path $galleryDir '01-before-cropped.png')
    Write-Bar $imgAfter  $afterBar  1.8 (Join-Path $galleryDir '02-after-cropped.png')

    # --- gallery 3-4: the same two moments, full screen, unedited -------------------------
    # Copied, not re-encoded: re-saving a JPEG would add generation loss for no gain, and
    # the point of these two is that they are exactly what the game looked like.
    Copy-Item $before (Join-Path $galleryDir '03-before-fullscreen.jpg') -Force
    Copy-Item $after  (Join-Path $galleryDir '04-after-fullscreen.jpg') -Force
    foreach ($n in '03-before-fullscreen.jpg', '04-after-fullscreen.jpg') {
        $len = (Get-Item (Join-Path $galleryDir $n)).Length
        Write-Host ("  {0,-42} {1,9:N0} bytes" -f $n, $len) -ForegroundColor Green
    }

    # --- thumbnail: 512x512 grid tile -----------------------------------------------------
    # A 1620x198 strip is unreadable as a square tile, so the tile is composed: the whole
    # bar for context, then a zoom on three panels that are actually cooling down.
    $bmp = New-Object System.Drawing.Bitmap 512, 512
    $g = New-Graphics $bmp
    $g.Clear($bg)

    Add-Text $g 'PERK COOLDOWN' 256 22 27 $yellow
    Add-Text $g 'HUD' 256 56 27 $yellow

    $barW = 480
    $barH = [int]($afterBar.Height * ($barW / $afterBar.Width))
    $g.DrawImage($imgAfter, (New-Object System.Drawing.Rectangle 16, 118, $barW, $barH), $afterBar, [System.Drawing.GraphicsUnit]::Pixel)

    $zoomW = 380
    $zoomH = [int]($coolingZoom.Height * ($zoomW / $coolingZoom.Width))
    $zoomRect = New-Object System.Drawing.Rectangle 66, 232, $zoomW, $zoomH
    $g.DrawImage($imgAfter, $zoomRect, $coolingZoom, [System.Drawing.GraphicsUnit]::Pixel)

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
    $imgBefore.Dispose()
    $imgAfter.Dispose()
}

Write-Host ''
Write-Host 'thumbnail.png is uploaded by mod_updateworkshopitem.' -ForegroundColor Yellow
Write-Host 'workshop-images\ must be attached by hand on the Workshop item page.' -ForegroundColor Yellow
