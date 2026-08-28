# CLAUDE.md — YBO Launcher

Working notes for this repo: architecture, conventions, and how to build and run.
The product requirements live in [SPEC.md](SPEC.md); read that first in a new session.

**Current state: Phase 8 (Hardening) complete. All eight phases done.** See "Phase status" at the bottom.

---

## What this is

A Windows 11 app launcher. Home shows every app on the PC with no setup; the user adds
tabs and drags apps into them. Unpackaged (no MSIX required), WinUI 3, .NET 9.

## Build and run

Requires the **.NET 9 SDK** (9.0.317 pinned in `global.json`). Windows only.

```bash
dotnet build YboLauncher.sln
```

```bash
dotnet test YboLauncher.sln
```

```bash
dotnet run --project src/Launcher.App/Launcher.App.csproj
```

The debug binary lands at
`src/Launcher.App/bin/Debug/net9.0-windows10.0.19041.0/win-x64/YboLauncher.exe`.

For the shippable standalone folder (no .NET runtime needed on the target machine):

```bash
dotnet publish src/Launcher.App/Launcher.App.csproj -c Release -r win-x64
```

### Deployment model

| Property | Value | Why |
| --- | --- | --- |
| `WindowsPackageType` | `None` | Runs unpackaged, no MSIX registration. |
| `WindowsAppSDKSelfContained` | `true` | Windows App SDK binaries ship in the output folder, so no runtime install step. |
| `SelfContained` | `false` in Debug, `true` in Release | Debug builds use the shared .NET 9 runtime and stay fast; Release is fully standalone. |

Debug output is ~140 MB because the Windows App SDK travels with it. Release self-contained
is larger again. That is the cost of "no installer, no prerequisites".

**Adding MSIX later** needs no source changes: add `Package.appxmanifest` plus logo assets,
then flip `WindowsPackageType` and `EnableMsixTooling`. Nothing in the app calls a
packaged-only API without an unpackaged fallback.

## Layout

```
src/
  Launcher.Core/        Models, storage, services. No WinUI/XAML references.
  Launcher.App/         WinUI 3 app: Views, ViewModels, Services, Styles.
  Launcher.Core.Tests/  xUnit.
```

`Launcher.Core` targets `net9.0-windows10.0.19041.0` — the `-windows` TFM is needed for the
**WinRT** projections app discovery uses in Phase 2 (`PackageManager`, `AppListEntry`).
Those are WinRT, not XAML. The rule "no UI references" means no `Microsoft.UI.Xaml`, so the
layer stays unit-testable off a UI thread. Keep it that way.

### Where things go

- Anything that could be tested without a window → `Launcher.Core`.
- Anything touching `Microsoft.UI.*` → `Launcher.App` (e.g. `ThemeService`).
- View models never reference XAML types; they talk to interfaces.

## Conventions

- Nullable reference types on, `TreatWarningsAsErrors` on. Analyzer level
  `latest-recommended`. Style rules from `.editorconfig` are IDE suggestions only
  (`EnforceCodeStyleInBuild=false`) so formatting nits never fail a build, but real
  compiler and analyzer warnings do.
- Private fields `_camelCase`, file-scoped namespaces, `var` only when the type is obvious.
- All I/O is `async`. No `.Result`, no `.Wait()`.
- Every filesystem and COM call is wrapped. A single broken shortcut or an access-denied
  folder must never take down a scan. `CA1031` (catch general exception) is disabled
  repo-wide for exactly this reason.
- Package versions are centralized in `Directory.Packages.props`
  (Central Package Management). Do not put versions in individual `.csproj` files.
- Shared build settings live in `Directory.Build.props`, including the single definition of
  the target framework (`$(WindowsTargetFramework)`).

## Persistence

State lives in `%LocalAppData%\YBO Launcher\`, or in `<app folder>\data\` when a
`portable.txt` sits next to the executable (portable mode). `StoragePaths` resolves this;
`StoragePaths.Resolve` is a pure function so it can be tested against fake directories.

`JsonStorageService` is the only thing that touches those files. Three rules it enforces:

1. **Atomic writes.** Serialize to `<file>.tmp`, flush to the device, then `File.Replace`.
   A crash mid-write leaves the previous file intact.
2. **Never throw on bad input.** A missing, locked, or corrupt file returns `null` so the
   caller falls back to defaults. Unparseable files are moved to `<file>.corrupt-<stamp>`
   rather than deleted.
3. **Versioned schema.** Document types carry `[SchemaVersion(n)]` and implement
   `IVersionedDocument`. On load, registered `IDocumentMigration`s are walked from the
   file's version up to the current one. Migrations operate on the raw `JsonObject`,
   because an old file may not deserialize into the current CLR type at all. A file
   written by a *newer* build is left untouched and ignored, never downgraded.

There are no migrations registered yet — everything is at v1. The mechanism is covered by
tests (`JsonStorageServiceTests`) including a v1→v2 rename, so the path is proven before
it is needed.

## Discovery

Sources implement `IAppSource` and are run concurrently by `AppDiscoveryService`, which
then filters, deduplicates, reconciles against the existing catalog, and saves `apps.json`.
A source that throws is logged and skipped; it never costs the other sources their results.

**Threading.** `IShellLink` and the shell image factory are apartment-threaded COM. The
whole Start Menu walk therefore runs on one dedicated STA thread (`StaThread.RunAsync`).
On a thread pool thread — which is MTA — every call would cross an apartment boundary
through a proxy, which across several hundred shortcuts is the difference between a fast
scan and a visibly slow one. The package catalog source has no such constraint and runs on
the thread pool, so the two overlap.

**Identity and merging.** `AppIdentity` computes a *merge key* answering "are these the
same app?", and the entry id is just its hash. Because the id is derived rather than
generated, a rescan re-derives it and user edits (rename, custom icon, launch counts) stay
attached — `AppEntry.UpdateFromScan` copies only discovery-owned fields. Key preference is
AUMID → URI → target path (+arguments) → name. Arguments are part of the key on purpose:
two shortcuts to the same binary with different switches are genuinely different apps.

**Filtering.** `JunkFilter` matches on whole words, not substrings — "Visual Studio
Installer" is a real app and "Uninstall Foo" is not. Rejected entries are *marked, not
dropped*, so the show-filtered-entries toggle reveals them with no rescan. Packaged apps
are never filtered: the catalog has no clutter in it, and names like "Get Help" would
otherwise trip the word list.

**Icons.** Cached as PNGs keyed on (source path, last-write time, size), so an app update
naturally invalidates its icon. Executables and shortcuts go through
`IShellItemImageFactory`; packaged apps use `AppListEntry.DisplayInfo.GetLogo`, which is
already a PNG and only needs copying. Icons are extracted from the `.lnk` rather than its
target so the shortcut's own icon location and index are honoured.

### Game launchers

`GameLauncherAppSource` is one `IAppSource` over several `IGameLibrary` implementations:
Steam, Epic, GOG, Ubisoft Connect, EA (Origin and EA Desktop), Battle.net, itch.io, Game
Jolt, Amazon Games, Rockstar and HoYoPlay. Each reads the launcher's own bookkeeping
rather than guessing at folders, and each returns an empty list when that launcher is absent — the
normal case — so adding another store is a new small class and a DI registration, nothing
else.

- **Steam** parses `libraryfolders.vdf` for every library root (games are routinely spread
  across drives) and one `appmanifest_*.acf` per app. `VdfParser` is a small, deliberately
  lenient reader for Valve's KeyValues format: Steam can be mid-write when we read, so a
  truncated file yields what was parsed rather than throwing. Only `StateFlags & 4`
  (fully installed) counts, and the shared redistributables and Proton/Linux runtimes are
  excluded by app id and name.
- **Epic** reads `%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item`, skipping
  incomplete installs and anything not categorised as a game (the same folder tracks
  engine installs and plugins).
- **EA** reads the `.mfst` files under `%ProgramData%\Origin\LocalContent` and
  `EA Desktop\LocalContent` and pulls the offer id out of their query string.
- **itch.io** has no readable index — the app keeps one SQLite database, and taking a
  SQLite dependency for a single launcher is not worth it. Every install carries its own
  `.itch\receipt.json.gz` instead (gzipped JSON with the title and classification), so the
  install folders are walked and the receipts read. itch hosts art packs, soundtracks and
  books alongside games; those classifications are skipped, as are web games, which run
  inside the itch app itself and cannot be started from outside it.
- **Game Jolt** reads the client's own store, `packages.wttf` and `games.wttf` in
  `%AppData%\game-jolt-client`. Both are JSON despite the extension, and have been written
  as a keyed map and as a plain array across client versions, so the reader accepts either
  rather than assuming one. A package is named after its game, not after its build.
- **Amazon Games** also keeps a SQLite index, so its per-user uninstall keys are read
  instead: one `AmazonGames/<title>` key per game, holding the display name, the install
  folder and — inside the uninstall command — the product id that
  `amazon-games://play/<id>` needs. Each install carries a `fuel.json` naming the real
  entry point, which is preferred over guessing at the folder.
- **Rockstar Games** records an install folder per title under `SOFTWARE\Rockstar Games`.
  Those games launch by executable, which is what the launcher's own Start Menu shortcuts
  do — the game's boot executable brings up the launcher for sign-in by itself. Keys under
  the same root for the launcher, Social Club and redistributables are skipped.
- **HoYoPlay** is read from two places at once: the install path HoYoPlay records per game
  under `Cognosphere\HYP` (global) or `miHoYo\HYP` (Chinese client), and the ordinary
  uninstall entries filtered by publisher. The overlap is deliberate — a game installed
  before HoYoPlay existed has only the uninstall entry — and duplicates collapse on the
  executable. Names come from the executable rather than the folder, which is named after
  the build ("Genshin Impact game") and differs between the Chinese and global clients
  (`YuanShen.exe` and `GenshinImpact.exe` are the same game). These titles launch by
  executable: there is no documented protocol for starting one, and the game brings up its
  own updater and sign-in anyway.
- **GOG, Ubisoft and Battle.net** are registry-based. GOG games are DRM-free, so they
  launch their executable directly; the other two go through their launcher's protocol.

**Launching** prefers the launcher's protocol (`steam://rungameid/…`,
`com.epicgames.launcher://…`, `uplay://…`, `origin2://…`, `amazon-games://play/…`) because
it is what starts the launcher's own overlay, cloud saves and DRM. The install path is
still kept on the entry: it is where the icon comes from and what makes "open file
location" work. GOG, itch.io, Game Jolt, Rockstar and HoYoPlay have no such protocol worth
going through - their games run directly, and for the last two the game's own executable
is what brings the launcher up - so for those the executable is the launch target, and an install
with no executable to find is skipped rather than shown as a tile that opens a folder.

**Deduplication is free.** A game's Start Menu shortcut is a `.url` holding the same
protocol URI, so `AppIdentity` derives the same merge key from both and they collapse into
one tile — the shortcut wins for its path, the library entry for its icon.

**Xbox Game Pass is deliberately absent from this source.** Those titles install as MSIX
packages and are already found by `PackagedAppSource`; a second source would only create
duplicates.

No game launcher is installed on the development machine, so this code is covered by
fixture tests that write a real folder layout to disk - a Steam library in
`GameLauncherAppSourceTests`, itch and Game Jolt installs in `IndieLauncherTests`, Amazon
and Rockstar installs in `StoreLauncherTests`, HoYoverse build folders in
`HoYoPlayLibraryTests` - rather than by a live scan. For the
registry-based launchers only the entry-to-game step is covered that way; the registry walk
itself needs an installed launcher. Every file name and data shape here comes from those
clients' published behaviour rather than from a machine that has them, so each reader is
deliberately lenient and reports nothing when what it finds does not match.

## Launching

`LaunchService` never throws — a failure is a `LaunchResult`, because SPEC.md requires an
InfoBar rather than a crash or a silent no-op. It also distinguishes *failed* from
*cancelled*: a user who clicks No on the UAC prompt (`ERROR_CANCELLED`, 1223) made a
decision, and showing them an error for it would be wrong.

Packaged apps are started through the package catalog, never by path. The `AppListEntry`
is re-found via `FindPackagesForUser("", familyName)` — a targeted lookup, not a full
enumeration — and there is a `shell:AppsFolder\<AUMID>` fallback for when the package has
been removed since the last scan. `CanLaunchAsAdministrator` is false for packaged apps
and links: `AppListEntry.LaunchAsync` has no elevation option, so offering the menu item
would be a lie.

**Tile actions.** Commands live on `AppTileViewModel` so `x:Bind` inside the item template
reaches them directly, but the work belongs to `HomeViewModel`, which owns the dialogs,
the InfoBar and persistence. `IAppTileHost` is the seam between the two.

**Hiding is reversible.** "Hide from Home" would otherwise be a one-way door, so the
`ShowHiddenEntries` setting brings hidden tiles back with the menu item flipped to "Show on
Home". Same shape as `ShowFilteredEntries`: entries are marked, never dropped.

Launch counts and user edits are written back to `apps.json` on an 800 ms debounce, so
launching several apps in a row does not mean several full rewrites.

## Tabs

`TabService` is the only thing that mutates the tab list, so the Home invariants live in
one place: Home always exists, is always index 0, and is never renamed, moved or deleted.
`Normalize` re-establishes all of that when loading, so a hand-edited or partially written
`tabs.json` — no Home, two Homes, duplicate ids — still yields a usable strip.

**Home membership is implicit.** Home shows every discovered app, so `AddEntriesAsync` and
`RemoveEntriesAsync` are no-ops for it; an app leaves Home by being hidden, not un-listed.
Home's `EntryIds` is an *order hint* rather than a membership list: anything missing from
it is appended alphabetically, which is what stops a newly installed app from vanishing
after the user has manually reordered.

**Drop rules** (SPEC.md): from Home it copies, because Home keeps everything; out of a
custom tab it moves, removing from the source only. Dropping a custom tab's tile onto Home
therefore just removes it from that tab, which falls out of the same rule. Drags carry
their payload in `DataPackage.Properties` (see `DragFormats`) — they never leave the
process, so there is no need for a registered clipboard format.

**Reorder** is detected from the `ObservableCollection` reporting a `Move`, which is the
only signal that distinguishes a user drag from a rebuild — hence `TabViewModel.IsRebuilding`
and `LibraryViewModel.IsSyncingTabs`, which suppress write-back during our own churn. A
manual reorder switches the tab to `SortMode.Manual`, or the new order would silently
revert on the next rebuild.
**Tab icons are monochrome, never emoji.** A tab's glyph is one Segoe Fluent Icons
character from the fixed set in `TabGlyphs`, chosen from a grid in the tab dialog — there
is no free-text icon field, because a text field is what let emoji in originally. Emoji
render as full-colour bitmaps that ignore the theme and sit at a different weight to every
other icon in the window, and they show as a missing-glyph box when the surrounding font
is the icon font. `TabGlyphs.IsFluentGlyph` is the single gate: `TabService.Normalize`
rewrites Home's glyph and drops any other non-Fluent glyph on load, so tab files written
by an older build migrate silently, and `SetAppearanceAsync` refuses one on the way in.

This applies only to the launcher's *own* iconography. Icons extracted for a discovered
app keep their real colours — that is the app's identity, not chrome.

The glyph set is built from code points via `char.ConvertFromUtf32` rather than from
literal characters in the source. Private-use characters do not survive every editing tool
(see Gotchas), and a silently emptied string is a blank icon at runtime that no test
catches.
## Search

Two layers, deliberately separated. `FuzzyMatcher` answers "how well does this pattern fit
this text?" and knows nothing about apps. `SearchService` turns that into a rank, folding
in where the match starts, how long the name is, and how the app is actually used.

**Matching** is fzf-style: subsequence required, with bonuses for word boundaries,
camelCase humps and consecutive characters, and penalties for gaps. Alignment is chosen by
dynamic programming, not greedily — for "st" in "Visual Studio" the first `s` that fits is
in "Vi**s**ual", but the right answer is "**St**udio".

One deliberate deviation from fzf: `BoundaryGapRefund` partly refunds the gap cost when the
character after a gap starts a word. Skipping whole words to match an acronym is a
different intent from skipping letters inside one, and fzf charges both the same.

**Ranking** adds three things the match score cannot express:

- *Where the match starts.* "Advanced **V**s **S**ettings" actually earns a slightly higher
  raw score for "vs" than "Visual Studio Code" — two initials, short gap — so the SPEC.md
  requirement that Visual Studio Code win depends entirely on preferring a match at the
  start of the name. That case is a test.
- *Exact and prefix bonuses.* Typing an app's whole name is unambiguous intent.
- *Usage,* capped at `MaxUsageBoost`. Frequency is logarithmic and recency decays
  exponentially, and the cap means usage breaks ties between plausible matches without ever
  overturning a clearly better one.

The executable's file name is searched as a secondary field behind a penalty, so "devenv"
finds Visual Studio without outranking anything whose visible name matches.

## Presentation, motion and accessibility

**View mode, tile size and sort are per tab**, stored on `LauncherTab` and changed from the
view button beside the tab strip. New tabs start from the defaults on the Settings page.

One tile definition (`AppTile`) serves both the wrapping grid and the compact list, so
there is a single context menu that cannot drift between modes. Layout differences are
expressed as XAML-typed properties on `TabViewModel` (`ItemOrientation`, `LabelAlignment`,
`ItemPadding`, …) rather than converters. `AppGridView` hosts a `GridView` and a `ListView`
and toggles between them — a collapsed list realizes no containers, so only the visible one
costs anything.

**Sorting does exactly what it says.** Favourites do not float to the top: a sort labelled
"A to Z" that is not actually A to Z is worse than no sort. The star badge marks them
instead. A manual drag switches the tab to `SortMode.Manual`, or the new order would revert
on the next rebuild.

**Motion** is centralised in `Motion`. Everything is ≤120 ms, well inside SPEC.md's 250 ms
cap. `Motion.AnimationsEnabled` folds together the Windows "show animations" setting and
high contrast, and when it is false animations are *skipped entirely* rather than shortened
— a reduced-motion user wants no movement, not faster movement. It is read once at startup
because it is queried on every pointer move, so changing the setting takes effect on the
next launch.

**High contrast** also suppresses the Mica backdrop: a translucent wallpaper-tinted surface
defeats the point of a high contrast palette.

**Keyboard.** Ctrl+F / Ctrl+K focus search; type anywhere to start searching; ↓/↑, Enter and
Esc drive the results; Ctrl+Tab and Ctrl+Shift+Tab cycle tabs with wraparound; Ctrl+1–9 jump
to a tab, where 9 means *last* rather than ninth, matching browsers; Enter launches and
Delete removes from a custom tab.

## System integration

**Global hotkey.** `RegisterHotKey` posts `WM_HOTKEY` to the window's message queue, and
WinUIEx's `WindowMessageMonitor` subclasses the HWND so it can be observed — WinUI exposes
no WndProc of its own. This works fine unpackaged; the MSIX fallback SPEC.md allows was not
needed. `MOD_NOREPEAT` is always set, or holding the combination fires continuously.
Conflict detection is the return value: `ERROR_HOTKEY_ALREADY_REGISTERED` (1409) becomes
`HotkeyStatus.AlreadyInUse`, which the Settings page shows in words. A binding needs at
least one modifier — registering a bare key would swallow it system-wide.

**Off by default.** A global hotkey takes a combination away from every other app, so it is
opt-in rather than something that happens on first run.

**Tray icon** lives in the window's XAML tree so its context menu inherits a `XamlRoot`.
That means a minimized start still has to `Activate()` before hiding, because WinUI does
not build a window's content until it is activated.

**Start with Windows** writes to `HKCU\...\Run` — per-user, so no elevation. The value is
always quoted (the path routinely contains spaces) and carries `--minimized` when the user
wants a tray-only start. `IsStale()` reports a Run entry pointing at a different copy of
the app, which happens when the folder is moved; re-enabling repairs it. The toggle reads
the registry rather than settings.json, so it reflects reality if the entry is removed
outside the app.

**Exit** is only on the tray menu once close-to-tray is on. `WindowService.RequestExit`
sets a flag the `AppWindow.Closing` handler checks, and the tray icon is disposed first or
it lingers in the notification area as a ghost until hovered.

## Hardening

**Staying fresh.** `AppWatcherService` watches both Start Menu folders with a
`FileSystemWatcher` and subscribes to `PackageCatalog` install/uninstall/update events.
Both are noisy — an installer writes a folder of shortcuts in a burst — so changes are
coalesced behind a 4-second settle timer and reported once. The watcher buffer is raised to
64 KB because the default overflows on a busy install, and an overflow drops events
silently; the error handler treats an overflow as a reason to rescan, which is exactly
right since a rescan re-reads everything anyway.

**Export and import.** `ConfigArchiveService` zips settings, tabs, the catalog and the icon
cache. An import backs up what was there first, so a wrong archive is undoable by importing
the backup. Entry paths are validated against zip-slip (`..`, absolute paths, drive
letters) and the archive is refused outright if it does not contain a launcher document —
a refused import writes nothing at all, not even a backup.

**Crash log.** An unhandled XAML exception is written to `crash-<stamp>.log` beside the
settings, then allowed to kill the process. Continuing after one means running in an
unknown state, and a launcher that quietly corrupts a layout is worse than one that stops.
This paid for itself immediately — see the PRI note below.

**Cold start**, Release, warm cache: window visible ~400 ms, tiles on screen ~950 ms. Well
inside SPEC.md's one-second target. The catalog loads from `apps.json` and icons decode
lazily as tiles scroll into view, so the count of installed apps barely moves the number.

## Dependency choices

**Windows App SDK is pinned to the 1.8 line, not 2.x**, even though 2.4.0 is current.
WinUIEx 2.9.3 depends on `Microsoft.WindowsAppSDK.WinUI` 1.8.x and
`CommunityToolkit.WinUI.Controls.SettingsControls` on 1.6.x. Neither has shipped a
2.x-compatible build, and mixing majors is a hard conflict. SPEC.md asks for "1.6+", which
1.8 satisfies. Revisit when the ecosystem moves.

`Microsoft.Extensions.*` is held at 9.0.x to match the net9.0 TFM (10.x targets .NET 10).

For Phase 7, note that **H.NotifyIcon.WinUI 2.4.1 is .NET 10 only**; 2.3.2 is the newest
version with a `net9.0-windows` target.

**System.Drawing.Common** is used only to encode extracted icons as PNG. It is a GDI+
imaging dependency, not a UI framework, so it does not violate the "no UI in
Launcher.Core" rule. `Image.FromHbitmap` is deliberately *not* used: it discards the alpha
channel, which turns every icon's antialiased edge into a black fringe. The pixels are
pulled out with `GetDIBits` into a top-down 32bpp buffer instead.

## Not built yet

- **Section headers inside a tab** (SPEC.md "Views & layout"). User-created groups within a
  tab need a section model on `LauncherTab`, grouped item sources, and UI to create, rename
  and assign into them — comparable in size to the whole tab feature. Deferred rather than
  half-built.
- **Connected animation** from a tile into the properties dialog. `ContentDialog` builds its
  content before it opens, which makes the destination element awkward to hand to
  `ConnectedAnimation`. The other motion in SPEC.md is implemented.
- **In-app accent override.** The launcher honours the Windows accent, which is what
  SPEC.md requires; the Settings row links to the Windows colour page rather than offering
  a picker that would need the whole accent brush ramp re-derived at runtime.

## Known deviations

- **`MVVMTK0045` is suppressed** in `Launcher.App.csproj`. The analyzer wants
  `[ObservableProperty]` on partial properties rather than fields, for NativeAOT safety in
  WinRT. We do not publish with NativeAOT, and more importantly
  CommunityToolkit.Mvvm 8.4.2's `ObservablePropertyGenerator` silently emits nothing for
  partial properties under Roslyn 4.14 in this project — `[RelayCommand]` generates
  correctly on the same types, so the generator is running. Field-based properties are used
  instead. Re-test when the toolkit is upgraded.
- **`CS0618` is suppressed for `*.g.cs`** via `.editorconfig`. The XAML-generated
  `XamlTypeInfo.g.cs` enumerates every property of every XAML-visible type, including
  `WinUIEx.WindowEx.Icon`, which is `[Obsolete]`. We do not control that file.

## Gotchas hit so far

- **Private-use characters get eaten by editing tools.** A `sed` pass over a source file
  silently stripped the Segoe Fluent Icons characters out of two glyph literals, leaving
  `IsHidden ? "" : ""`. It compiles, no test fails, and the icon is simply blank at
  runtime. Anywhere a glyph is chosen in code it is now written as a code point —
  `char.ConvertFromUtf32(0xE71C)` — and XAML uses `&#xE71C;`. `od -c` is the way to check
  what is actually in the file; the terminal shows nothing either way.
- `ColumnDefinition.Width` is a `GridLength`. Binding an `x:Double` resource to it compiles
  fine and then fails at runtime with `0x802B000A` (XAML parse error) — which surfaces as a
  bare `STATUS_STOWED_EXCEPTION` crash with no managed stack. If the app dies instantly on
  launch, suspect a XAML type mismatch and check the Application event log.
- Subscribe to `AppWindow.Changed` *before* restoring window placement, or the startup
  geometry change is missed and a first run never writes `settings.json`.
- Window geometry is saved on a 750 ms debounce during the session, not on `Closed` —
  a fire-and-forget async save on close races process shutdown and loses the write.
- **An AUMID on a shortcut does not mean the app is packaged.** Plenty of ordinary desktop
  apps stamp `PKEY_AppUserModel_ID` on their Start Menu shortcuts purely so the taskbar
  groups their windows: Firefox uses `308046B0AF4A39CB`, Edge `MSEdge`, Visual Studio
  `VisualStudio.257105d1`. Treating those as packaged discards the shortcut's target path
  and leaves an entry nothing can launch. `PackagedAppId.IsPackagedAumid` requires the real
  `Name_PublisherId!AppId` shape.
- `IShellLink::GetPath` returns nothing for shortcuts to a shell folder — File Explorer,
  Control Panel, Run. They are real apps, so the target falls back to the `.lnk` itself;
  ShellExecute on a shortcut resolves the ID list without any extra interop.
- `IShellLink::Resolve` is never called. Its "find the moved target" search can hit the
  network or trigger an MSI repair, costing seconds per shortcut, and a shortcut whose
  target is gone is junk anyway.
- **A `GridViewItem` takes its automation name from the bound item's `ToString()`.** Without
  an override, Narrator announces every tile as
  `Launcher.App.ViewModels.AppTileViewModel`. `AutomationProperties.Name` on the template
  root is not enough on its own — the container is a separate element. Verified with UI
  Automation, not by assumption.
- **`AutoSuggestBox` swallows Enter, Escape and the arrow keys** for its own suggestion
  list, so the title bar search box is a plain `TextBox`. Even then, `TextBox` marks the
  arrow keys handled during its own focus navigation, so the search box uses the tunneling
  **`PreviewKeyDown`** rather than `KeyDown` — a bubbling handler never sees Down/Up.
- **A `TwoWay` binding on `ListView.SelectedIndex` fights list repopulation.** Clearing the
  items resets `SelectedIndex` to -1, which the binding writes straight back over the view
  model. `SearchResultsView` pushes selection by hand behind a re-entrancy guard instead.
- XAML event handlers **cannot be `static`** — the generated code binds them through an
  instance reference and fails with CS0176.
- **`dotnet publish` drops the app's own `.pri` for an unpackaged WinUI build.** It is
  produced into `bin\` but never reaches the publish folder, and without it
  `Application.LoadComponent` cannot find any compiled XAML — the app dies at the first
  `InitializeComponent` with a bare `XamlParseException` and exit code `0xC000027B`. The
  build output runs fine, so this only appears in a *published* copy. Fixed by the
  `PublishAppResourceIndex` target in `Launcher.App.csproj`. **Always smoke-test the
  publish output, not just the build.**
- **`Content` alone does not copy a file to the output folder** in an unpackaged build; it
  needs `CopyToOutputDirectory` as well, or `AppWindow.SetIcon` finds nothing at runtime.
- **Compiled-binding converters do not work in a XAML file whose root is a `Window`.** The
  generated code calls `SetConverterLookupRoot(this)`, which needs a `FrameworkElement`;
  a `Window` is not one, and it fails with a confusing CS1503 inside `MainWindow.g.cs`.
  MainWindow therefore binds `Visibility`-typed view model properties directly. Converters
  are fine inside `AppGridView`, which is a `UserControl`.
- `DialogService` gets the window through `Attach`, not the constructor. Page view models
  resolve it while `MainWindow` is still being constructed, so a `MainWindow` dependency
  would re-enter the container mid-construction. `NavigationView.SelectedItem` is likewise
  set on `Loaded`, not in the constructor, so no page is built during window construction.

## Phase status

| Phase | State |
| --- | --- |
| 1. Skeleton | **Done** — window, custom titlebar, Mica, theme switching, DI, nav shell |
| 2. Discovery | **Done** — Start Menu + package catalog, dedupe, junk filter, icon cache |
| 3. Home tab | **Done** — virtualized grid, tiles, launching, context menu, InfoBar |
| 4. Tabs | **Done** — create/rename/reorder/delete, drag-and-drop, Explorer drop |
| 5. Search | **Done** — fzf-style matcher, ranked results, full keyboard flow |
| 6. Polish | **Done** — view modes, sizing, sorting, motion, keyboard, accessibility |
| 7. System integration | **Done** — tray, global hotkey, run at startup, full settings |
| 8. Hardening | **Done** — live refresh, export/import, crash log, perf pass, README |

### Registered services

Every service interface now has a real implementation; nothing is bound to a placeholder.

| Interface | Implementation |
| --- | --- |
| `IStorageService` | `JsonStorageService` |
| `ISettingsService` | `SettingsService` |
| `IIconService` | `IconService` |
| `IAppDiscoveryService` | `AppDiscoveryService` |
| `IAppSource` (multiple) | `StartMenuAppSource`, `PackagedAppSource`, `GameLauncherAppSource` |
| `IGameLibrary` (multiple) | `SteamLibrary`, `EpicLibrary`, `GogLibrary`, `UbisoftLibrary`, `EaLibrary`, `BattleNetLibrary`, `ItchLibrary`, `GameJoltLibrary`, `AmazonGamesLibrary`, `RockstarLibrary` |
| `ILaunchService` | `LaunchService` |
| `IStartupService` | `StartupService` (HKCU Run key) |
| `IAppWatcherService` | `AppWatcherService` (FileSystemWatcher + PackageCatalog) |
| `IConfigArchiveService` | `ConfigArchiveService` (export/import zip) |
| `ITabService` | `TabService` |
| `ISearchService` | `SearchService` (`FuzzyMatcher` is static) |
| `IThemeService` | `ThemeService` (app layer — touches `Microsoft.UI.Xaml`) |
| `IDialogService` | `DialogService` (app layer — needs `XamlRoot` and the HWND) |
| `IWindowService` | `WindowService` (show/hide/exit, app layer) |
| `IHotkeyService` | `HotkeyService` (app layer — needs the HWND) |
| `StoragePaths`, `ShellLinkResolver`, `JunkFilter`, `UserEntryFactory` | concrete singletons |
