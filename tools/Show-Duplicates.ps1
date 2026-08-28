<#
.SYNOPSIS
    Prints apps that appear more than once in the catalog, and why.

.DESCRIPTION
    Two tiles for one app means two entries whose merge keys differ. This shows each
    duplicated name together with the fields the key is built from, which is what says
    whether the cause is a second shortcut, a separate packaged copy, or something the
    deduplicator should have caught.

    Reads only; nothing is changed. Paste the output into an issue.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Show-Duplicates.ps1
#>

[CmdletBinding()]
param(
    # Defaults to the installed location; pass this for a portable copy.
    [string] $CatalogPath = "$env:LOCALAPPDATA\YBO Launcher\apps.json"
)

if (-not (Test-Path $CatalogPath)) {
    Write-Error "No catalog at $CatalogPath. Run the launcher once, or pass -CatalogPath."
    exit 1
}

$catalog = Get-Content $CatalogPath -Raw | ConvertFrom-Json
$entries = @($catalog.entries)

"catalog:  $CatalogPath"
"written:  $((Get-Item $CatalogPath).LastWriteTime)"
"entries:  $($entries.Count)"
""

$groups = $entries |
    Group-Object { ($_.displayName -replace '\s+', ' ').Trim().ToLowerInvariant() } |
    Where-Object { $_.Count -gt 1 }

if (-not $groups) {
    "No duplicated names."
    exit 0
}

"duplicated names: $($groups.Count)"

foreach ($group in $groups) {
    ""
    "=== $($group.Group[0].displayName) ==="

    foreach ($entry in $group.Group) {
        "  source=$($entry.source)  launchKind=$($entry.launchKind)  id=$($entry.id)"
        "    mergeKey = $($entry.mergeKey)"
        "    target   = $($entry.targetPath)"
        "    args     = $($entry.arguments)"
        "    uri      = $($entry.launchUri)"
        "    aumid    = $($entry.appUserModelId)"
        "    shortcut = $($entry.shortcutPath)"
        "    icon     = $($entry.iconCacheFile)"
    }
}
