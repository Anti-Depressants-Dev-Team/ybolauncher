# CLAUDE.md — YBO Launcher

Working notes for this repo: architecture, conventions, and how to build and run.
The product requirements live in [SPEC.md](SPEC.md); read that first in a new session.

**Current state: Phase 1 (Skeleton) complete.** See "Phase status" at the bottom.

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

## Dependency choices

**Windows App SDK is pinned to the 1.8 line, not 2.x**, even though 2.4.0 is current.
WinUIEx 2.9.3 depends on `Microsoft.WindowsAppSDK.WinUI` 1.8.x and
`CommunityToolkit.WinUI.Controls.SettingsControls` on 1.6.x. Neither has shipped a
2.x-compatible build, and mixing majors is a hard conflict. SPEC.md asks for "1.6+", which
1.8 satisfies. Revisit when the ecosystem moves.

`Microsoft.Extensions.*` is held at 9.0.x to match the net9.0 TFM (10.x targets .NET 10).

For Phase 7, note that **H.NotifyIcon.WinUI 2.4.1 is .NET 10 only**; 2.3.2 is the newest
version with a `net9.0-windows` target.

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

## Phase status

| Phase | State |
| --- | --- |
| 1. Skeleton | **Done** — window, custom titlebar, Mica, theme switching, DI, nav shell |
| 2. Discovery | Not started |
| 3. Home tab | Not started |
| 4. Tabs | Not started |
| 5. Search | Not started |
| 6. Polish | Not started |
| 7. System integration | Not started |
| 8. Hardening | Not started |

### Registered services (Phase 1)

Only services with a real implementation are in the container. `IAppDiscoveryService`,
`IIconService`, `ILaunchService` and `ISearchService` are **deliberately absent** rather
than bound to do-nothing placeholders; they arrive with Phases 2, 2, 3 and 5.

| Interface | Implementation |
| --- | --- |
| `IStorageService` | `JsonStorageService` |
| `ISettingsService` | `SettingsService` |
| `IThemeService` | `ThemeService` (app layer — touches `Microsoft.UI.Xaml`) |
| `StoragePaths` | concrete singleton |
