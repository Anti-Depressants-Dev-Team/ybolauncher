# CLAUDE.md — YBO Launcher

Working notes for this repo: architecture, conventions, and how to build and run.
The product requirements live in [SPEC.md](SPEC.md); read that first in a new session.

**Current state: Phase 3 (Home tab) complete.** See "Phase status" at the bottom.

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
- XAML event handlers **cannot be `static`** — the generated code binds them through an
  instance reference and fails with CS0176.
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
| 4. Tabs | Not started |
| 5. Search | Not started |
| 6. Polish | Not started |
| 7. System integration | Not started |
| 8. Hardening | Not started |

### Registered services

Only services with a real implementation are in the container. `ISearchService` is
**deliberately absent** rather than bound to a do-nothing placeholder; it arrives with
Phase 5.

| Interface | Implementation |
| --- | --- |
| `IStorageService` | `JsonStorageService` |
| `ISettingsService` | `SettingsService` |
| `IIconService` | `IconService` |
| `IAppDiscoveryService` | `AppDiscoveryService` |
| `IAppSource` (multiple) | `StartMenuAppSource`, `PackagedAppSource` |
| `ILaunchService` | `LaunchService` |
| `IThemeService` | `ThemeService` (app layer — touches `Microsoft.UI.Xaml`) |
| `IDialogService` | `DialogService` (app layer — needs `XamlRoot` and the HWND) |
| `StoragePaths`, `ShellLinkResolver`, `JunkFilter` | concrete singletons |
