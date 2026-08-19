<#
.SYNOPSIS
    Checks a mod's Workshop description against the mechanical rules in
    WORKSHOP-DESCRIPTION-SOP.md.

.DESCRIPTION
    Checks only what a script can check: length against Steam's 8,000 character limit,
    how much lands above the "Read More" fold, BBCode tag validity and balance, non-ASCII
    characters, and the presence of a source link and a bold opening hook.

    It cannot tell you whether the writing is any good. Read section 8 of the SOP for the
    part that needs a human.

.EXAMPLE
    .\tools\check-description.ps1 -Mod PerkCooldownHud

.EXAMPLE
    .\tools\check-description.ps1          # every mod under src\
#>
[CmdletBinding()]
param(
    [string] $Mod,
    [string] $Path
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

# Steam's ISteamUGC::SetItemDescription limit. Community-reported, consistent across SDK
# wrappers; Valve does not publish it on a linkable doc page.
$MaxChars = 8000

# The fold is a CSS line clamp, not a character count, so it moves with font and zoom.
# The SOP budgets 250 rather than the commonly cited ~300 for that reason.
$FoldBudget = 250

# Confirmed to render in a Workshop item description.
$KnownTags = @(
    'b', 'i', 'u', 'strike', 'h1', 'h2', 'h3', 'hr',
    'list', 'olist', '*', 'table', 'tr', 'th', 'td',
    'url', 'img', 'code', 'quote', 'spoiler', 'noparse', 'previewyoutube'
)

# Tags that exist elsewhere on Steam, or not at all, and render as literal text here.
$BadTags = @{
    'color'       = 'Steam BBCode has no colour tag; it will render as literal text.'
    'size'        = 'Steam BBCode has no size tag; use [h1]/[b] instead.'
    'font'        = 'not a Steam BBCode tag.'
    'center'      = 'not a Steam BBCode tag.'
    'video'       = 'use [previewyoutube] with the YouTube video id.'
    'previewimg'  = 'a community-post tag; it does not work in item descriptions.'
    'previewicon' = 'a community-post tag; it does not work in item descriptions.'
    'screenshot'  = 'a community-post tag; it does not work in item descriptions.'
    'emoticon'    = 'a community-post tag; it does not work in item descriptions.'
}

# Undocumented by Valve: they work today but could be withdrawn.
$RiskyTags = @('h2', 'h3')

$SelfClosing = @('hr', '*', 'br')

function Get-DescriptionFiles {
    if ($Path) {
        if (-not (Test-Path -LiteralPath $Path)) { throw "No such file: $Path" }
        return @((Resolve-Path -LiteralPath $Path).Path)
    }

    $srcDir = Join-Path $repoRoot 'src'
    $dirs = if ($Mod) {
        @(Join-Path $srcDir $Mod)
    } else {
        @(Get-ChildItem -LiteralPath $srcDir -Directory | ForEach-Object { $_.FullName })
    }

    $found = @()
    foreach ($d in $dirs) {
        $f = Join-Path $d 'workshop-description.txt'
        if (Test-Path -LiteralPath $f) { $found += $f }
        else { Write-Host "  (no workshop-description.txt in $d)" -ForegroundColor DarkGray }
    }
    return $found
}

function Get-RenderedText {
    param([string] $Text)
    $stripped = [regex]::Replace($Text, '\[/?[a-zA-Z0-9*]+[^\]]*\]', '')
    return ([regex]::Replace($stripped, '\s+', ' ')).Trim()
}

function Test-Description {
    param([string] $File)

    $errors = @()
    $warnings = @()

    $text = [IO.File]::ReadAllText($File)
    $rendered = Get-RenderedText $text

    # --- length -----------------------------------------------------------------
    if ($text.Length -gt $MaxChars) {
        $errors += "Length is $($text.Length) characters; Steam's limit is $MaxChars. Markup counts."
    }

    # --- the fold ---------------------------------------------------------------
    $fold = if ($rendered.Length -le $FoldBudget) { $rendered } else { $rendered.Substring(0, $FoldBudget) }
    if ($rendered.Length -lt 40) {
        $errors += 'There is almost no rendered text here. Did the file get truncated?'
    }

    # --- opening hook -----------------------------------------------------------
    if ($text -notmatch '^\s*\[b\]') {
        $warnings += 'Does not open with a [b] hook. The SOP wants one bold sentence first.'
    }
    $firstSentence = ($rendered -split '(?<=[.!?])\s', 2)[0]
    if ($firstSentence -match '^\s*(I|We|My|Hi|Hey|Hello|This is my)\b') {
        $errors += "Opens in first person: `"$firstSentence`" -- the SOP forbids a first-person opener."
    }
    if ($firstSentence -match '\?\s*$') {
        $warnings += 'Opens with a rhetorical question. That reads as filler on a utility mod.'
    }
    if ($firstSentence.Length -gt 120) {
        $warnings += "First sentence is $($firstSentence.Length) characters. Aim for one short verb phrase."
    }

    # --- BBCode -----------------------------------------------------------------
    $stack = New-Object System.Collections.Generic.Stack[string]
    $seen = @{}
    foreach ($m in [regex]::Matches($text, '\[(/)?([a-zA-Z0-9*]+)([^\]]*)\]')) {
        $closing = $m.Groups[1].Value -eq '/'
        $name = $m.Groups[2].Value.ToLowerInvariant()
        $seen[$name] = $true

        if ($BadTags.ContainsKey($name)) {
            $errors += "[$name] does not work here -- $($BadTags[$name])"
            continue
        }
        if ($KnownTags -notcontains $name) {
            $errors += "[$name] is not a tag that renders in a Workshop item description."
            continue
        }
        if ($SelfClosing -contains $name) { continue }

        if ($closing) {
            if ($stack.Count -eq 0) { $errors += "Stray closing tag [/$name]." }
            elseif ($stack.Peek() -ne $name) {
                $errors += "Tag [$($stack.Peek())] is closed by [/$name]."
                $null = $stack.Pop()
            }
            else { $null = $stack.Pop() }
        }
        else { $stack.Push($name) }
    }
    while ($stack.Count -gt 0) { $errors += "Tag [$($stack.Pop())] is never closed." }

    foreach ($r in $RiskyTags) {
        if ($seen.ContainsKey($r)) {
            $warnings += "[$r] works but is undocumented by Valve. [b] is the safe heading."
        }
    }
    if ($seen.ContainsKey('h1') -and $seen.ContainsKey('b')) {
        $warnings += 'Mixes [h1] with [b] headings. Pick one; the house style is [b].'
    }

    # --- ASCII ------------------------------------------------------------------
    $nonAscii = [regex]::Matches($text, '[^\x09\x0A\x0D\x20-\x7E]')
    if ($nonAscii.Count -gt 0) {
        $chars = ($nonAscii | ForEach-Object { $_.Value } | Sort-Object -Unique) -join ' '
        $errors += "$($nonAscii.Count) non-ASCII character(s): $chars -- keep the description pure ASCII."
    }

    # --- required content -------------------------------------------------------
    if ($text -notmatch '\[url=') {
        $warnings += 'No [url] link. Every serious mod here links its source.'
    }
    if ($rendered -notmatch '(?i)\b(subscribe|subscribing)\b') {
        $warnings += 'Never says how to install it. One line saying "Subscribe" is enough.'
    }
    if ($rendered -notmatch '(?i)(save|campaign|mid-campaign|unsubscrib)') {
        $warnings += 'Says nothing about save safety. That is the question players ask most.'
    }

    # --- voice ------------------------------------------------------------------
    if ($rendered -match '(?i)\b(seamless(ly)?|immersive|game-?changing|revolutionary|must-have|amazing|awesome|epic)\b') {
        $warnings += 'Marketing adjective present. The surveyed utility mods contain none.'
    }
    $caps = [regex]::Matches($rendered, '\b[A-Z]{4,}\b') |
        ForEach-Object { $_.Value } |
        Where-Object { $_ -notin @('HUD', 'MCM', 'JSON', 'TRUE', 'FALSE', 'BBCODE') } |
        Sort-Object -Unique
    if ($caps.Count -gt 0) { $warnings += "ALL CAPS words: $($caps -join ', ')." }
    if ($rendered -match '(?i)\bchange ?log\b') {
        $warnings += 'Looks like a changelog. Steam has a Change Notes tab; keep it out of the body.'
    }

    # --- report -----------------------------------------------------------------
    $rel = $File.Replace("$repoRoot\", '')
    Write-Host ''
    Write-Host $rel -ForegroundColor Cyan
    Write-Host ("  {0} characters of {1} ({2:P0} of budget), {3} rendered" -f `
        $text.Length, $MaxChars, ($text.Length / $MaxChars), $rendered.Length)
    Write-Host '  Above the fold:' -ForegroundColor DarkGray
    Write-Host "    $fold" -ForegroundColor Gray

    foreach ($w in $warnings) { Write-Host "  WARN  $w" -ForegroundColor Yellow }
    foreach ($e in $errors) { Write-Host "  FAIL  $e" -ForegroundColor Red }
    if (-not $errors -and -not $warnings) { Write-Host '  OK' -ForegroundColor Green }

    return $errors.Count
}

$files = Get-DescriptionFiles
if (-not $files) { Write-Host 'No workshop-description.txt files found.' -ForegroundColor Yellow; exit 0 }

$failed = 0
foreach ($f in $files) { $failed += Test-Description $f }

Write-Host ''
if ($failed -gt 0) {
    Write-Host "$failed problem(s) must be fixed." -ForegroundColor Red
    exit 1
}
Write-Host 'Mechanical checks passed. Now read it against section 8 of the SOP.' -ForegroundColor Green
exit 0
