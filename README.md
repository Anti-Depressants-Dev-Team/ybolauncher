# YBO Launcher

A Windows 11 app launcher. Open it and every app on the PC is already there — no setup, no
manual adding. From there you make as many tabs as you like and drag apps into them.

Built with WinUI 3 on .NET 9, shipped unpackaged: one folder, no installer, no
prerequisites on the target machine.

---

## What it does

**Finds everything by itself.** On first run it scans both Start Menu folders, the
Store/MSIX package catalog and your game launchers, deduplicates the results down to one
entry per app, filters out the clutter (uninstallers, readme links, dead shortcuts), and
caches every icon. Newly installed apps appear on their own — a file watcher and the
package catalog tell it when something changed.

**Your games too.** Steam, Epic, GOG, Ubisoft Connect, EA and Battle.net libraries are
read from each launcher's own records, so installed games appear alongside everything
else and start through their launcher — overlay, cloud saves and all. Xbox Game Pass
titles arrive through the package catalog. Switch the whole thing off in Settings if you
do not want it.

**Tabs you arrange.** Home always holds every app and can't be deleted. Every other tab is
yours: name it, pick an icon and an accent colour, drag apps in from Home, drag files
and folders in from Explorer, reorder by dragging, and delete it without touching a single
app.

**Search that gets out of the way.** Start typing anywhere. Matching is fzf-style — a
subsequence match scored on word boundaries and consecutive runs, weighted by how often and
how recently you launch things. `↓`/`↑` walk the results, `Enter` launches, `Esc` clears.

**Summon it from anywhere.** An optional global hotkey (default `Alt+Space`) shows and
hides the window from any app. A tray icon keeps it out of the way, and the close button
can hide instead of quitting.

---

## Requirements

| | |
| --- | --- |
| To run | Windows 10 1809 (17763) or later, x64 |
| To build | [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) — 9.0.317 is pinned in `global.json` |

A published build carries its own .NET runtime and Windows App SDK, so a machine that only
*runs* the launcher needs nothing installed.

---

## Build and run

```bash
dotnet build YboLauncher.sln
```

```bash
dotnet test YboLauncher.sln
```

```bash
dotnet run --project src/Launcher.App/Launcher.App.csproj
```

### Producing a release build

```bash
dotnet publish src/Launcher.App/Launcher.App.csproj -c Release -r win-x64 -o publish/win-x64
```

That writes a self-contained folder (~216 MB, because the .NET runtime and the Windows App
SDK travel with it). Copy the folder anywhere and run `YboLauncher.exe` — there is nothing
to install.

For ARM64, swap `-r win-x64` for `-r win-arm64`.

---

## Where your data lives

`%LocalAppData%\YBO Launcher\`

| File | Holds |
| --- | --- |
| `settings.json` | Preferences and window geometry |
| `tabs.json` | Your tabs and what is in them |
| `apps.json` | The discovered app catalog, plus your renames, custom icons and launch counts |
| `iconcache\` | Extracted icons, as PNGs |

Every write is atomic — a crash mid-save leaves the previous file intact — and a file that
cannot be read is moved aside rather than deleted, so a bad file never costs you the rest.

**Portable mode.** Put an empty file called `portable.txt` next to `YboLauncher.exe` and
everything is stored in `<app folder>\data\` instead. Nothing is written outside the app
folder, so it will run from a USB stick.

**Backups.** Settings → Storage → *Export* writes the whole configuration to one zip.
*Import* restores it, and saves what was there to a backup zip first, so a wrong import can
be undone by importing that backup.

---

## Keyboard

| | |
| --- | --- |
| Any letter | Jump to search |
| `Ctrl+F` / `Ctrl+K` | Focus search |
| `↓` `↑` | Move through search results |
| `Enter` | Launch |
| `Esc` | Clear search |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | Next / previous tab |
| `Ctrl+1`–`Ctrl+9` | Jump to a tab (`9` is the last one) |
| `Delete` | Remove from the current tab — never uninstalls |
| `Alt+Space` | Show or hide the launcher, once the global hotkey is turned on |

---

## Source layout

```
src/
  Launcher.Core/        Models, storage, discovery, search, launching. No WinUI references.
  Launcher.App/         WinUI 3 app: Views, ViewModels, Controls, Services.
  Launcher.Core.Tests/  xUnit — 210 tests.
```

`Launcher.Core` holds everything that can be tested without a window, which is most of the
interesting logic: the fuzzy matcher, deduplication, the junk filter, tab rules and the
storage layer.

[CLAUDE.md](CLAUDE.md) documents the architecture and the decisions behind it.
[SPEC.md](SPEC.md) is the original specification.

---

## Not implemented

- **Section headers within a tab.** Grouping inside a single tab.
- **MSIX packaging.** The project is structured so it can be added without source changes —
  add `Package.appxmanifest` and logo assets, then flip two properties.
