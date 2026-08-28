# SPEC — WinUI 3 App Launcher

> Source spec as provided by the user. Re-read this file at the start of any new session.

Build a Windows desktop **app launcher** from scratch.

## Stack (non-negotiable)

- **C# / .NET 9**, **WinUI 3** on **Windows App SDK 1.6+**
- **MVVM** using `CommunityToolkit.Mvvm` (source generators: `[ObservableProperty]`, `[RelayCommand]`)
- `CommunityToolkit.WinUI.Controls.*` for `SettingsCard`, `SettingsExpander`, etc.
- `WinUIEx` for window management, `H.NotifyIcon.WinUI` for the tray icon
- Ship **unpackaged / self-contained** by default (single folder, no MSIX install required), but keep the project structured so MSIX packaging can be added later without refactoring
- Target Windows 10 1809+ and Windows 11
- No WPF, no WinForms, no Electron, no web views for the main UI

## The core idea

Open the app → you're on a **Home tab that already has every app on your PC in it**, no setup, no manual adding. From there the user creates as many extra tabs as they want to organize things however they like (Games, Work, Media, Dev, whatever) and drags apps into them.

## App discovery (this has to Just Work on first launch)

On first run, scan in the background and populate Home:

1. **Start Menu shortcuts** — `%ProgramData%\Microsoft\Windows\Start Menu\Programs` and `%AppData%\Microsoft\Windows\Start Menu\Programs`, recursively. Resolve `.lnk` targets, arguments, working dir, and icon location via `IShellLink`.
2. **Packaged / Store / UWP apps** — `PackageManager.FindPackagesForUser("")` → `Package.GetAppListEntries()`. Launch these via `AppListEntry.LaunchAsync()`, **not** by path.
3. **Optionally (behind a settings toggle):** Steam (parse `libraryfolders.vdf` + `appmanifest_*.acf`, launch via `steam://rungameid/{id}`), Epic (`%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item`), Xbox Game Pass.

Rules for the scan:

- **Deduplicate aggressively.** One entry per app. Merge a Start Menu `.lnk` and its packaged equivalent into a single entry.
- **Filter the junk by default** — uninstallers, readme/help/manual links, "Documentation", broken shortcuts pointing at missing targets, and web link shortcuts inside vendor folders. Keep a settings toggle to show them again.
- Extract icons at high DPI: `SHGetFileInfo` / `IShellItemImageFactory` for exes and shortcuts, `AppListEntry.DisplayInfo.GetLogo()` for packaged apps. **Cache them as PNGs** in `%LocalAppData%` keyed by a hash of the source path + mtime. Never re-extract icons on every launch.
- The scan runs **async and off the UI thread**, with a progress indicator, and the window is usable while it runs.
- Keep it fresh: `FileSystemWatcher` on the Start Menu directories plus `PackageCatalog` events, so newly installed apps appear without a manual refresh. There's also a manual "Rescan" button in Settings.

## Tabs

- **Home** is special: it always contains every discovered app, can't be deleted or renamed, and is always first.
- User can **create, rename, reorder (drag), and delete** any other tab.
- Each custom tab gets an optional **emoji or Fluent icon** and an optional accent color.
- **Drag and drop apps between tabs.** Dragging from Home into a tab *copies* the app into that tab (Home keeps everything). Dragging within a tab reorders. Dragging out of a custom tab removes it from that tab only.
- **Drag files/folders/shortcuts in from Explorer** to add custom entries to the current tab.
- Deleting a tab asks for confirmation and never deletes the underlying apps.
- Use `TabView` (or a custom tab strip) with the Fluent look — scrollable when there are many tabs, with an "+" button at the end.

## Launching & per-app options

Right-click (and a "..." button on the tile) gives a context menu:

- Launch / **Launch as administrator**
- Open file location
- Pin to a tab (submenu listing tabs) / Unpin from this tab
- Rename (display name only, original preserved)
- Change icon (pick an image or another exe's icon)
- Edit launch arguments + working directory
- Add to favorites
- Hide from Home
- Properties (path, target, size, last launched, launch count)

Launch failures show a non-blocking `InfoBar`, never a crash or a silent no-op.

## Search

- **Just start typing** anywhere in the app and it focuses a search box (plus `Ctrl+F` / `Ctrl+K`).
- **Fuzzy matching** with fzf-style scoring: subsequence match, bonuses for word-boundary and consecutive-character hits, plus a weighting for launch frequency and recency, so typing `vs` puts Visual Studio Code above "Advanced Vs Settings".
- Search covers **all tabs** by default, with a toggle to scope it to the current tab. Results highlight the matched characters.
- `Enter` launches the top result, `↓`/`↑` moves through results, `Esc` clears.
- Put the fuzzy matcher in its own class with **unit tests** — it's the piece most likely to feel wrong.

## Views & layout

- Per-tab view mode: **large grid / medium grid / compact list**, with a tile-size slider.
- Sort: manual (drag order), A–Z, most used, recently used.
- Use `ItemsRepeater` or a virtualized `GridView` — must stay smooth with 500+ entries.
- Optional section headers inside a tab (user-created groups within a tab).

## Look and feel

This should look like a first-party Windows 11 app, not a school project:

- **Mica backdrop** (Mica Alt as an option), fully custom titlebar with the app icon, title, search box, and window buttons — `ExtendsContentIntoTitleBar`
- Rounded corners, correct Fluent corner radii, proper spacing scale (4/8/12/16/24)
- **Dark / Light / Follow system** theme, and honor the user's Windows accent color
- Real animation: implicit show/hide on tiles, connected animation when opening app details, a subtle scale+shadow on hover, smooth tab transitions. Nothing bouncy or slow — everything under 250ms.
- Empty states with an illustration + a call to action (e.g. an empty custom tab says "Drag apps here from Home").
- Full **keyboard navigation**: arrow keys across the grid, `Ctrl+Tab`/`Ctrl+Shift+Tab` between tabs, `Ctrl+1`–`Ctrl+9` to jump to a tab, `Enter` to launch, `Delete` to remove from tab. Visible focus rings everywhere.
- **Accessibility**: `AutomationProperties.Name` on every interactive element, works with Narrator, respects reduced-motion and high-contrast.

## System integration

- **Global hotkey** to summon/hide the launcher (default `Alt+Space`, rebindable in Settings, with conflict detection)
- **Tray icon** with Show / Rescan / Settings / Exit; option to minimize to tray instead of closing
- **Start with Windows** toggle (registry `Run` key for unpackaged builds)
- Optional "hide window after launching an app"
- Remember window size, position, and last active tab

## Persistence

- JSON under `%LocalAppData%\<AppName>\`: `apps.json`, `tabs.json`, `settings.json`, plus an `iconcache\` folder
- **Atomic writes** (write to temp file → `File.Replace`) so a crash mid-save never corrupts the user's layout
- **Versioned schema** with a migration path
- **Export / Import** the whole config as a single zip from Settings
- A **portable mode**: if a `portable.txt` sits next to the exe, store everything in the app folder instead

## Settings page

Build with `SettingsCard` / `SettingsExpander`: theme, accent, default view mode, tile size, startup behavior, global hotkey, tray behavior, which discovery sources are enabled, show-hidden-entries toggle, manage hidden apps, clear icon cache, rescan, export/import, reset to defaults, about + version.

## Architecture

```
src/
  Launcher.App/          # WinUI 3 app: Views, ViewModels, Controls, Converters
  Launcher.Core/         # Models, services, discovery, fuzzy search, persistence — no UI refs
  Launcher.Core.Tests/   # xUnit
```

- `Launcher.Core` must not reference any WinUI/XAML types so it's testable in isolation.
- Services behind interfaces (`IAppDiscoveryService`, `IIconService`, `IStorageService`, `ILaunchService`, `ISearchService`) registered in a DI container (`Microsoft.Extensions.DependencyInjection`).
- Everything I/O bound is `async`. No `.Result`, no `.Wait()`, no blocking the UI thread.
- Every filesystem/COM call is wrapped — a single broken shortcut or an access-denied folder must never take down the scan.

## Build this in phases

Get a **compiling, runnable build at the end of every phase**, and run `dotnet build` and the tests before telling me a phase is done. Stop after each phase and let me try it.

1. **Skeleton** — WinUI 3 window, custom titlebar, Mica, theme switching, DI, navigation shell. Nothing but the shell.
2. **Discovery** — Start Menu + packaged app scanning, dedupe, icon extraction + cache. Dump results to a plain list to prove it works.
3. **Home tab** — virtualized grid, tiles, launching, context menu.
4. **Tabs** — create/rename/reorder/delete, drag-and-drop between tabs, persistence.
5. **Search** — fuzzy matcher + tests, search UI, keyboard flow.
6. **Polish** — animations, empty states, view modes, sorting, accessibility pass.
7. **System integration** — tray, global hotkey, run at startup, settings page.
8. **Hardening** — error handling audit, perf pass (cold start under 1s with a warm cache), export/import, README + build instructions.

## Ground rules

- Write a `CLAUDE.md` at the repo root documenting the architecture, conventions, and how to build/run, and keep it updated as you go.
- Nullable reference types on, warnings as errors, `.editorconfig` committed.
- Meaningful commits at each phase.
- Don't stub things out and tell me they're done. If a piece is genuinely hard (COM interop for `.lnk` resolution, global hotkeys in an unpackaged WinUI 3 app), say so and propose the approach before burning time on it.
- Prefer boring, working code over clever code.

## Appendix — user's own notes (not part of the requirements)

- If unpackaged WinUI 3 + global hotkeys proves troublesome, the sanctioned fallback is a packaged (MSIX) build with a self-signed dev cert.
- The spec is delivered phase by phase, not all at once.
