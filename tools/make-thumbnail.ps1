<#
.SYNOPSIS
    Builds src\PerkCooldownHud\thumbnail.png (the Steam Workshop preview image).

.DESCRIPTION
    mod_updateworkshopitem is the ONLY command that uploads a preview, and it reads exactly
    <contentPath>\thumbnail.png. build.ps1 stages src\<Mod>\thumbnail.png into dist\<Mod>\,
    so this script writes it next to the source.

    The raw captures are 5120x1440 ultrawide, where the effect bar is a ~1000px sliver lost
    in a mostly black frame - unusable as a square grid tile. So we crop the bar, scale it
    with nearest-neighbour (it is pixel art; bilinear turns it to mush) and caption it.

    Regenerating is cheap, so this is a script rather than a binary checked in blind.
#>
[CmdletBinding()]
param(
    [string]$Source = 'C:\Users\pompe\Downloads\Examples\20260818161922_1.jpg',
    [string]$OutFile = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\PerkCooldownHud\thumbnail.png')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $Source)) { throw "Source screenshot not found: $Source" }

# Regions measured from the 5120x1440 capture.
$stripRect = New-Object System.Drawing.Rectangle 2040, 30, 1060, 130   # the whole effect bar
$zoomRect  = New-Object System.Drawing.Rectangle 2830, 42, 268, 106    # just the two cooling panels

$size = 512
$bg     = [System.Drawing.ColorTranslator]::FromHtml('#14120F')
$yellow = [System.Drawing.ColorTranslator]::FromHtml('#E9D24B')
$muted  = [System.Drawing.ColorTranslator]::FromHtml('#9AA79A')

$src = [System.Drawing.Image]::FromFile($Source)
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)

try {
    $g.Clear($bg)

    # Pixel art: keep the hard edges.
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half

    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = [System.Drawing.StringAlignment]::Center

    $titleFont = New-Object System.Drawing.Font 'Consolas', 27, ([System.Drawing.FontStyle]::Bold)
    $capFont   = New-Object System.Drawing.Font 'Consolas', 15, ([System.Drawing.FontStyle]::Bold)
    $subFont   = New-Object System.Drawing.Font 'Consolas', 13, ([System.Drawing.FontStyle]::Regular)

    $yellowBrush = New-Object System.Drawing.SolidBrush $yellow
    $mutedBrush  = New-Object System.Drawing.SolidBrush $muted

    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.DrawString('PERK COOLDOWN', $titleFont, $yellowBrush, ($size / 2), 22, $fmt)
    $g.DrawString('HUD', $titleFont, $yellowBrush, ($size / 2), 56, $fmt)

    # Full effect bar, scaled to the card width.
    $stripW = 480
    $stripH = [int]($stripRect.Height * ($stripW / $stripRect.Width))
    $stripDest = New-Object System.Drawing.Rectangle ([int](($size - $stripW) / 2)), 116, $stripW, $stripH
    $g.DrawImage($src, $stripDest, $stripRect, [System.Drawing.GraphicsUnit]::Pixel)

    # Zoom on the cooling-down panels - the whole point of the mod.
    $zoomW = 380
    $zoomH = [int]($zoomRect.Height * ($zoomW / $zoomRect.Width))
    $zoomDest = New-Object System.Drawing.Rectangle ([int](($size - $zoomW) / 2)), 225, $zoomW, $zoomH
    $g.DrawImage($src, $zoomDest, $zoomRect, [System.Drawing.GraphicsUnit]::Pixel)

    $pen = New-Object System.Drawing.Pen $yellow, 2
    $g.DrawRectangle($pen, $zoomDest)

    $capY = $zoomDest.Bottom + 26
    $g.DrawString('YELLOW = ON COOLDOWN', $capFont, $yellowBrush, ($size / 2), $capY, $fmt)
    $g.DrawString('NUMBER = TICKS UNTIL READY', $subFont, $mutedBrush, ($size / 2), ($capY + 30), $fmt)
    $g.DrawString('vanilla just hides the icon', $subFont, $mutedBrush, ($size / 2), ($capY + 56), $fmt)

    $bmp.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $g.Dispose()
    $bmp.Dispose()
    $src.Dispose()
}

$len = (Get-Item $OutFile).Length
Write-Host ("wrote {0} ({1:N0} bytes)" -f $OutFile, $len) -ForegroundColor Green
if ($len -gt 1MB) { Write-Warning "Steam rejects preview images over 1 MB." }
