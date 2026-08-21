<#
.SYNOPSIS
    Builds the Steam Workshop preview image and gallery images for BaneCounter.

.DESCRIPTION
    Writes:
      src\BaneCounter\thumbnail.png          the grid tile; mod_updateworkshopitem uploads
                                             exactly this filename
      src\BaneCounter\workshop-images\*      gallery shots, attached by hand on the item
                                             page - no console command uploads these

    The gallery is three cropped shots, one per surface the mod touches, followed by the
    untouched full-screen captures they came from. Workshop subscribers reasonably distrust
    a listing made only of zoomed-in fragments, so the full-screen set is copied
    byte-for-byte rather than re-encoded and stays a genuine unedited screenshot.

    Source captures are 5120x1440 ultrawide, where every one of these elements is a small
    sliver in a mostly dark frame - unreadable once Steam scales it into a square grid tile.
    Crops are scaled nearest-neighbour because the game is pixel art and bilinear smears it.

    On the missing before/after pair: the honest "before" for the HUD icon is vanilla
    printing 1 where this mod prints 192, because CurseEffect.ViewValue returns
    CursesPower.Count - the number of active curse tiers - and not CurseData.CurseLevel.
    That comparison needs a vanilla capture of the same scene, which we do not have. It is
    described in the store text instead of being faked here; a composed "before" would be a
    claim about the game invented in an image editor, which is exactly what this project
    does not do. Drop vanilla captures in $SourceDir and extend this script if they ever
    get taken.
#>
[CmdletBinding()]
param(
    [string]$SourceDir = 'C:\Users\pompe\Downloads\Bane Examples',
    [string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\BaneCounter'),

    # The Steam Workshop display title, one array element per rendered line. This is the
    # store title, NOT UniqueModName - the two are deliberately different ("Bane Counter"
    # vs "BaneCounter"). Keep it in step with the title on the item page; a thumbnail that
    # names the mod something else is the single most obvious way for a listing to look
    # abandoned.
    [string[]]$TitleLines = @('BANE COUNTER')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# The three surfaces, one capture each.
$hudShot    = Join-Path $SourceDir 'Bane Icon Example.jpg'      # mission HUD, Bane 192
$rosterShot = Join-Path $SourceDir 'Bane Icon Example 2.jpg'    # Manage Operators + hover tooltip
$tipShot    = Join-Path $SourceDir 'Bane Tooltip Example.jpg'   # full operator tooltip

foreach ($f in @($hudShot, $rosterShot, $tipShot)) {
    if (-not (Test-Path $f)) { throw "Source screenshot not found: $f" }
}

# Regions measured from the 5120x1440 captures.
$hudIcon    = New-Object System.Drawing.Rectangle 2518, 53, 84, 86      # just the debuff icon
$hudContext = New-Object System.Drawing.Rectangle 2330, 20, 360, 180    # icon in its surroundings
$rosterAll  = New-Object System.Drawing.Rectangle 2080, 300, 1440, 760  # roster + hover tooltip
$rosterRow  = New-Object System.Drawing.Rectangle 2168, 676, 810, 66    # one operator row
$tipPanel   = New-Object System.Drawing.Rectangle 2530, 575, 745, 855   # the operator tooltip

$bg      = [System.Drawing.ColorTranslator]::FromHtml('#0E0F0C')
$yellow  = [System.Drawing.ColorTranslator]::FromHtml('#E9D24B')
$crimson = [System.Drawing.ColorTranslator]::FromHtml('#C41A43')
$muted   = [System.Drawing.ColorTranslator]::FromHtml('#9AA79A')

function Save-Png($bmp, [string]$path) {
    $dir = Split-Path -Parent $path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $len = (Get-Item $path).Length
    Write-Host ("  {0,-42} {1,9:N0} bytes" -f (Split-Path -Leaf $path), $len) -ForegroundColor Green
    if ($len -gt 1MB) { Write-Warning "$path exceeds Steam's 1 MB preview limit." }
}

# The sources are already JPEG, so a PNG crop of one stores lossy artefacts losslessly and
# can easily land several times larger than the frame it came from - the 1:1 roster crop
# came out at 1.1 MB. Where that happens, re-encode as high-quality JPEG: there is no
# fidelity left to protect, and Steam's 1 MB ceiling is real. Upscaled pixel-art crops
# normally compress fine as PNG and keep their hard edges, so this only fires when needed.
function Save-Image($bmp, [string]$path) {
    $dir = Split-Path -Parent $path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)

    if ((Get-Item $path).Length -gt 1MB) {
        $jpegPath = [IO.Path]::ChangeExtension($path, '.jpg')
        $codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() |
            Where-Object { $_.MimeType -eq 'image/jpeg' }
        $params = New-Object System.Drawing.Imaging.EncoderParameters 1
        $params.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter(
            [System.Drawing.Imaging.Encoder]::Quality, [long]94)
        $bmp.Save($jpegPath, $codec, $params)
        $params.Dispose()
        Remove-Item $path -Force
        $path = $jpegPath
    }

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

# A straight crop, scaled up. No overlay - these elements speak for themselves.
function Write-Crop($img, $srcRect, [double]$scale, [string]$path) {
    $w = [int]($srcRect.Width * $scale)
    $h = [int]($srcRect.Height * $scale)
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = New-Graphics $bmp
    $g.DrawImage($img, (New-Object System.Drawing.Rectangle 0, 0, $w, $h), $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    Save-Image $bmp $path
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

$imgHud    = [System.Drawing.Image]::FromFile($hudShot)
$imgRoster = [System.Drawing.Image]::FromFile($rosterShot)
$imgTip    = [System.Drawing.Image]::FromFile($tipShot)
$galleryDir = Join-Path $OutDir 'workshop-images'

try {
    if (Test-Path $galleryDir) { Remove-Item (Join-Path $galleryDir '*') -Force }

    Write-Host 'Writing images:' -ForegroundColor Cyan

    # --- gallery 1-3: one crop per surface -----------------------------------------------
    Write-Crop $imgHud    $hudContext 2.4 (Join-Path $galleryDir '01-hud-icon.png')
    Write-Crop $imgRoster $rosterAll  1.0 (Join-Path $galleryDir '02-operator-roster.png')
    Write-Crop $imgTip    $tipPanel   1.0 (Join-Path $galleryDir '03-operator-tooltip.png')

    # --- gallery 4-6: the same three moments, full screen, unedited ----------------------
    # Copied, not re-encoded: re-saving a JPEG adds generation loss for no gain, and the
    # entire point of these is that they are exactly what the game looked like.
    $fullScreens = [ordered]@{
        '04-hud-fullscreen.jpg'      = $hudShot
        '05-roster-fullscreen.jpg'   = $rosterShot
        '06-tooltip-fullscreen.jpg'  = $tipShot
    }
    foreach ($name in $fullScreens.Keys) {
        $dest = Join-Path $galleryDir $name
        Copy-Item $fullScreens[$name] $dest -Force
        Write-Host ("  {0,-42} {1,9:N0} bytes" -f $name, (Get-Item $dest).Length) -ForegroundColor Green
    }

    # --- thumbnail: 512x512 grid tile ----------------------------------------------------
    # The mod's whole signal is a three-digit number on a 22px icon. At browse-card size
    # that is a smudge, so the tile leads with the HUD icon blown up until the digits are
    # unmissable, then shows the roster row underneath to say it is per-operator too.
    $bmp = New-Object System.Drawing.Bitmap 512, 512
    $g = New-Graphics $bmp
    $g.Clear($bg)

    $titleY = 16
    foreach ($line in $TitleLines) {
        Add-Text $g $line 256 $titleY 30 $yellow
        $titleY += 38
    }

    $rulePen = New-Object System.Drawing.Pen $crimson, 3
    $g.DrawLine($rulePen, 120, ($titleY + 6), 392, ($titleY + 6))
    $rulePen.Dispose()

    # The icon, scaled to fill: 84x86 source -> 186x190.
    $iconRect = New-Object System.Drawing.Rectangle 163, 82, 186, 190
    $g.DrawImage($imgHud, $iconRect, $hudIcon, [System.Drawing.GraphicsUnit]::Pixel)

    Add-Text $g 'YOUR ACTUAL BANE LEVEL' 256 286 16 $yellow
    Add-Text $g 'vanilla shows the tier count, not this' 256 312 12 $muted 'Regular'

    # One operator row: 810x66 source -> 470x38.
    $rowRect = New-Object System.Drawing.Rectangle 21, 350, 470, 38
    $g.DrawImage($imgRoster, $rowRect, $rosterRow, [System.Drawing.GraphicsUnit]::Pixel)
    $rowPen = New-Object System.Drawing.Pen $crimson, 1
    $g.DrawRectangle($rowPen, $rowRect)
    $rowPen.Dispose()

    Add-Text $g 'ON EVERY OPERATOR, BEFORE YOU DEPLOY' 256 404 13 $yellow
    Add-Text $g 'hover for the distance to the next curse' 256 430 12 $muted 'Regular'

    $g.Dispose()
    Save-Png $bmp (Join-Path $OutDir 'thumbnail.png')
    $bmp.Dispose()
}
finally {
    $imgHud.Dispose()
    $imgRoster.Dispose()
    $imgTip.Dispose()
}

Write-Host ''
Write-Host 'thumbnail.png is uploaded by mod_updateworkshopitem.' -ForegroundColor Yellow
Write-Host 'workshop-images\ must be attached by hand on the Workshop item page.' -ForegroundColor Yellow
