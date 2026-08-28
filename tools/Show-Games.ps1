<#
.SYNOPSIS
    Reports which game launchers are installed and which of their games the launcher found.

.DESCRIPTION
    When a game is missing from the Games tab the cause is one of two things: the launcher
    was never detected, or it was detected and its games were not read. This checks the
    same places the app checks, then lists what actually landed in the catalog, so the two
    can be compared side by side.

    Reads only; nothing is changed. Paste the output into an issue.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Show-Games.ps1
#>

[CmdletBinding()]
param(
    [string] $CatalogPath = "$env:LOCALAPPDATA\YBO Launcher\apps.json"
)

function Test-Launcher {
    param([string] $Name, [scriptblock] $Probe)

    $found = $false
    $detail = ''

    try {
        $result = & $Probe
        if ($result) { $found = $true; $detail = "$result" }
    } catch {
        $detail = "probe failed: $($_.Exception.Message)"
    }

    '{0,-16} {1,-5} {2}' -f $Name, $(if ($found) { 'yes' } else { 'no' }), $detail
}

'=== launchers this machine has ==='

Test-Launcher 'Steam' {
    (Get-ItemProperty 'HKCU:\SOFTWARE\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
}
Test-Launcher 'Epic' {
    $p = "$env:ProgramData\Epic\EpicGamesLauncher\Data\Manifests"
    if (Test-Path $p) { "$p ($((Get-ChildItem $p -Filter *.item -ErrorAction SilentlyContinue).Count) manifests)" }
}
Test-Launcher 'GOG' {
    (Get-ChildItem 'HKLM:\SOFTWARE\WOW6432Node\GOG.com\Games' -ErrorAction SilentlyContinue).Count
}
Test-Launcher 'Ubisoft' {
    (Get-ChildItem 'HKLM:\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs' -ErrorAction SilentlyContinue).Count
}
Test-Launcher 'EA' {
    @("$env:ProgramData\Origin\LocalContent", "$env:ProgramData\EA Desktop\LocalContent") |
        Where-Object { Test-Path $_ } | ForEach-Object { $_ }
}
Test-Launcher 'Battle.net' {
    (Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue |
        Where-Object { $_.GetValue('Publisher') -match 'Blizzard' }).Count
}
Test-Launcher 'itch.io' {
    $p = "$env:APPDATA\itch\apps"
    if (Test-Path $p) { "$p ($((Get-ChildItem $p -Directory -ErrorAction SilentlyContinue).Count) installs)" }
}
Test-Launcher 'Game Jolt' {
    $p = "$env:APPDATA\game-jolt-client"
    if (Test-Path $p) { (Get-ChildItem $p -Filter *.wttf -ErrorAction SilentlyContinue).Name -join ', ' }
}
Test-Launcher 'Amazon' {
    (Get-ChildItem 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue |
        Where-Object { $_.PSChildName -like 'AmazonGames/*' }).Count
}
Test-Launcher 'Rockstar' {
    (Get-ChildItem 'HKLM:\SOFTWARE\WOW6432Node\Rockstar Games' -ErrorAction SilentlyContinue).PSChildName -join ', '
}
Test-Launcher 'HoYoPlay' {
    @('HKCU:\Software\Cognosphere\HYP', 'HKCU:\Software\miHoYo\HYP') |
        Where-Object { Test-Path $_ } | ForEach-Object { $_ }
}
Test-Launcher 'Riot' {
    $p = "$env:ProgramData\Riot Games"
    if (Test-Path $p) { (Get-ChildItem "$p\Metadata" -Directory -ErrorAction SilentlyContinue).Name -join ', ' }
}

''
'=== what the catalog holds ==='

if (-not (Test-Path $CatalogPath)) {
    Write-Warning "No catalog at $CatalogPath. Run the launcher once, or pass -CatalogPath."
    exit 0
}

$entries = @((Get-Content $CatalogPath -Raw | ConvertFrom-Json).entries)
"catalog: $CatalogPath"
"written: $((Get-Item $CatalogPath).LastWriteTime)"
"entries: $($entries.Count)"
''

# source 6 is AppSource.GameLauncher; isGame survives a merge, source does not.
$games = $entries | Where-Object { $_.isGame -or $_.source -eq 6 }
"entries marked as games: $($games.Count)"

foreach ($game in $games | Sort-Object displayName) {
    "  $($game.displayName)"
    "      isGame=$($game.isGame) source=$($game.source) kind=$($game.launchKind)"
    "      target=$($game.targetPath)"
    "      args=$($game.arguments)  uri=$($game.launchUri)"
}
