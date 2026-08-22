<#
.SYNOPSIS
    Builds the Steam Workshop preview image and gallery images for PerkListSort,
    published as "Alphabetize Class Perks".

.DESCRIPTION
    Writes:
      src\PerkListSort\thumbnail.png         the grid tile; mod_updateworkshopitem uploads
                                             exactly this filename
      src\PerkListSort\workshop-images\*     gallery shots, attached by hand on the item
                                             page - no console command uploads these

    This is the one mod in the repo where a before/after pair is not merely the preferred
    format but the entire pitch. A screenshot of an alphabetical list says nothing on its
    own; the two side by side say everything, because the column of first letters goes from
    A B A F B G T C to A A A A B B B B and needs no caption to be understood.

    The two sources were captured differently - the "before" is a crop of the perk window
    taken while the mod was off, the "after" is a full 5120x1440 frame - but both are at
    native resolution, so the cropped panels pair without one looking softer than the other.
    Neither is rescaled here beyond the tile composition.

    Crops are scaled nearest-neighbour because the game is pixel art and bilinear smears it.
#>
[CmdletBinding()]
param(
    [string]$BeforeShot = 'C:\Users\pompe\Downloads\Perk Sort Examples\before-vanilla-order.png',
    [string]$AfterShot  = 'C:\Users\pompe\Downloads\Alphabetize Class Perks\20260822111913_1.jpg',
    [string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\PerkListSort'),

    # The Steam Workshop display title, one array element per rendered line. This is the
    # store title, NOT UniqueModName - the two are deliberately different ("Alphabetize
    # Class Perks" vs "PerkListSort"). Keep it in step with the title on the item page; a
    # thumbnail that names the mod something else is the single most obvious way for a
    # listing to look abandoned.
    [string[]]$TitleLines = @('ALPHABETIZE', 'CLASS PERKS')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

foreach ($f in @($BeforeShot, $AfterShot)) {
    if (-not (Test-Path $f)) { throw "Source screenshot not found: $f" }
}

# The perk window, measured in each capture's own coordinates.
$beforePanel = New-Object System.Drawing.Rectangle 1015, 95, 440, 740
$afterPanel  = New-Object System.Drawing.Rectangle 3014, 306, 450, 768

$bg     = [System.Drawing.ColorTranslator]::FromHtml('#0B0E0C')
$yellow = [System.Drawing.ColorTranslator]::FromHtml('#E9D24B')
$teal   = [System.Drawing.ColorTranslator]::FromHtml('#4E8F72')
$muted  = [System.Drawing.ColorTranslator]::FromHtml('#8C998C')

function Save-Image($bmp, [string]$path) {
    $dir = Split-Path -Parent $path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)

    # A PNG crop of a JPEG source stores lossy artefacts losslessly and can end up larger
    # than the frame it came from. Where that breaks Steam's 1 MB preview ceiling there is
    # no fidelity left to protect, so re-encode.
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

$imgBefore = [System.Drawing.Image]::FromFile($BeforeShot)
$imgAfter  = [System.Drawing.Image]::FromFile($AfterShot)
$galleryDir = Join-Path $OutDir 'workshop-images'

try {
    if (Test-Path $galleryDir) { Remove-Item (Join-Path $galleryDir '*') -Force }

    Write-Host 'Writing images:' -ForegroundColor Cyan

    # --- gallery 1-2: the perk window, mod off and mod on --------------------------------
    Write-Crop $imgBefore $beforePanel 1.0 (Join-Path $galleryDir '01-before-vanilla-order.png')
    Write-Crop $imgAfter  $afterPanel  1.0 (Join-Path $galleryDir '02-after-alphabetical.png')

    # --- gallery 3-4: the untouched captures ---------------------------------------------
    # Copied, not re-encoded: the point of these is that they are exactly what the game
    # looked like. 03 is a genuine uncropped full-screen frame; 04 is named for what it
    # actually is, a crop of the window, because calling it a full-screen shot would be a
    # small lie in the one place this project cannot afford them.
    Copy-Item $AfterShot  (Join-Path $galleryDir '03-after-fullscreen.jpg') -Force
    Copy-Item $BeforeShot (Join-Path $galleryDir '04-before-window-capture.png') -Force
    foreach ($n in '03-after-fullscreen.jpg', '04-before-window-capture.png') {
        $len = (Get-Item (Join-Path $galleryDir $n)).Length
        Write-Host ("  {0,-42} {1,9:N0} bytes" -f $n, $len) -ForegroundColor Green
    }

    # --- thumbnail: 512x512 grid tile ----------------------------------------------------
    # Two panels side by side. No arrows, no annotation on the lists themselves: the column
    # of first letters is the whole argument and decorating it would only obscure it.
    $bmp = New-Object System.Drawing.Bitmap 512, 512
    $g = New-Graphics $bmp
    $g.Clear($bg)

    $titleY = 8
    foreach ($line in $TitleLines) {
        Add-Text $g $line 256 $titleY 25 $yellow
        $titleY += 32
    }

    $rulePen = New-Object System.Drawing.Pen $teal, 2
    $g.DrawLine($rulePen, 110, 76, 402, 76)
    $rulePen.Dispose()

    Add-Text $g 'BEFORE' 142 84 14 $muted
    Add-Text $g 'AFTER' 370 84 14 $yellow

    $panelW = 208
    $panelH = 350
    $panelY = 108

    $beforeRect = New-Object System.Drawing.Rectangle 38, $panelY, $panelW, $panelH
    $afterRect  = New-Object System.Drawing.Rectangle 266, $panelY, $panelW, $panelH
    $g.DrawImage($imgBefore, $beforeRect, $beforePanel, [System.Drawing.GraphicsUnit]::Pixel)
    $g.DrawImage($imgAfter, $afterRect, $afterPanel, [System.Drawing.GraphicsUnit]::Pixel)

    $framePen = New-Object System.Drawing.Pen $teal, 1
    $g.DrawRectangle($framePen, $beforeRect)
    $g.DrawRectangle($framePen, $afterRect)
    $framePen.Dispose()

    Add-Text $g 'THE SAME LIST, IN AN ORDER' 256 470 14 $yellow
    Add-Text $g 'you can actually search' 256 492 13 $muted 'Regular'

    $g.Dispose()
    Save-Image $bmp (Join-Path $OutDir 'thumbnail.png')
    $bmp.Dispose()
}
finally {
    $imgBefore.Dispose()
    $imgAfter.Dispose()
}

Write-Host ''
Write-Host 'thumbnail.png is uploaded by mod_updateworkshopitem.' -ForegroundColor Yellow
Write-Host 'workshop-images\ must be attached by hand on the Workshop item page.' -ForegroundColor Yellow
