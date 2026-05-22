# CLAUDE.md — Development Guide

## Project overview

Remnant Save Guardian is a WPF desktop application (.NET 8, C#) that backs up Remnant 2 save
files and visualises world rolls. It supports both Steam (`Saved Games\Remnant2\Steam\`) and
Xbox Game Pass (WGS package folder, synced via `ParseContainersIndex`).

Fork of [Razzmatazzz/RemnantSaveGuardian](https://github.com/Razzmatazzz/RemnantSaveGuardian).
Upstream changes can be pulled via `git fetch upstream`.

---

## Essential commands

```powershell
# Restore, build, test, publish — run in order; each step depends on the previous
dotnet restore RemnantSaveGuardian.sln
dotnet build   RemnantSaveGuardian.sln --no-restore -c Release
dotnet test    RemnantSaveGuardian.Tests/RemnantSaveGuardian.Tests.csproj --no-build -c Release
dotnet publish RemnantSaveGuardian/RemnantSaveGuardian.csproj --no-build -c Release -o "./publish"
```

**Always publish after any functional change.** The shortcut points to `publish\RemnantSaveGuardian.exe`.
Do not ask the user to rebuild manually — run the publish step yourself.

The app locks its DLLs while running. If publish fails with a file-locked error, ask the user
to close the app first.

---

## Project structure

```
RemnantSaveGuardian/
  RemnantSave.cs          — save file model; WGS sync/restore/parse logic lives here
  SaveWatcher.cs          — FileSystemWatcher wrappers (staging + WGS source)
  Logger.cs               — file + UI logging; writes to AppData\Local\RemnantSaveGuardian\logs\
  SaveBackup.cs           — backup entry model
  RemnantCharacter.cs     — character data parsed from save
  RemnantWorldEvent.cs    — world/event data parsed from save
  ViewModels/             — MVVM view models (CommunityToolkit)
  Views/Pages/            — BackupsPage, WorldAnalyzerPage, SettingsPage, LogPage
  Views/Windows/          — MainWindow (startup logic, WGS detection, watcher setup)
  Properties/             — Settings.settings / Settings.Designer.cs / App.config
                            (keep all three in sync when changing setting defaults)

RemnantSaveGuardian.Tests/
  WgsSyncTests.cs         — xUnit tests for WGS sync/parse/restore; use temp dirs, no mocks
```

**Key paths at runtime (all under `%LOCALAPPDATA%\RemnantSaveGuardian\`):**

| Folder | Purpose |
|---|---|
| `Saves\` | Staging folder — WGS GUID files copied here as `profile.sav` / `save_N.sav` |
| `Backups\` | Timestamped backup snapshots |
| `logs\` | `log_yyyy-MM-dd.txt`, rotates daily |

---

## Testing

Coverage is currently poor. **Write tests for every new piece of logic.**

### Rules

- All tests live in `RemnantSaveGuardian.Tests/`.
- Use isolated temp directories (`Path.GetTempPath()` + `Guid.NewGuid()`); clean up in
  `IDisposable.Dispose`.
- Test the real logic — do not mock the filesystem; write actual files and assert on actual
  output. This project has been bitten by mock/real divergence before.
- Name tests `MethodName_Condition_ExpectedBehaviour` (e.g.
  `SyncFromWgs_ThrowsOnEmptySource_LeavesDestUntouched`).
- Tests must pass before publish. CI enforces this; so should you locally.

### What to test

- Any method that reads/writes files or parses binary data.
- Error paths and boundary conditions, not just the happy path.
- When adding platform logic (new save source, new file format), write tests first.

### What not to test

- WPF UI behaviour — use manual testing for that.
- Third-party library internals.
- Settings serialisation — covered by the framework.

---

## Design principles

### SOLID — apply where the benefit is clear, not as ceremony

- **Single Responsibility** — `RemnantSave.cs` owns save file logic; `SaveWatcher.cs` owns
  watching; `Logger.cs` owns logging. Do not let `MainWindow.xaml.cs` accumulate business
  logic — push it down into the appropriate class.
- **Open/Closed** — prefer adding new save-source support (e.g. Epic) by extending
  `RemnantSave` with a new static method rather than adding conditionals to the UI layer.
- **Dependency Inversion** — `SaveWatcher` and `RemnantSave` static methods are currently
  easy to call but hard to inject. When adding logic that needs testing, favour accepting
  paths/delegates as parameters over hardcoding `Properties.Settings.Default` calls inside
  library methods.

### KISS — the codebase has a lot of existing complexity; don't add to it

- Solve the specific problem. Do not design for hypothetical future requirements.
- Three similar lines is better than a premature abstraction.
- If a method is already doing too much, leave it — refactor is a separate task with its own
  PR, not a side effect of a bug fix.

### DRY — eliminate genuine duplication, not incidental similarity

- Hardcoded paths (`%LOCALAPPDATA%\RemnantSaveGuardian\...`) must come from the constants in
  `RemnantSave.cs` (`DefaultWgsStagingFolder`, `DefaultBackupFolder`, `DefaultWgsSaveFolder`).
  Do not repeat them inline.
- The WGS staging folder path is referenced in `MainWindow`, `BackupsPage`, and `SaveWatcher`.
  Any new reference must use the constant.
- Settings defaults exist in three places (`Settings.settings`, `App.config`,
  `Settings.Designer.cs`). When changing a default, update all three.

---

## Desktop app practices

### UI thread
- All UI updates must happen on the dispatcher: `Dispatcher.Invoke(() => { ... })`.
- `SaveWatcher` events fire on background threads — never touch WPF controls directly from
  a watcher callback.
- Long-running file operations (sync, backup) must not block the UI thread.

### Error handling
- Validate at system boundaries: user input, file system reads, binary parsing.
- Use `Logger.Error()` for anything the user should know about, `Logger.Log()` for internal
  diagnostics. Do not swallow exceptions silently in production paths — at minimum log them.
- `SyncFromWgs` and `RestoreToWgs` are the two most dangerous operations (they touch live
  save data). Both already have safety mechanisms — do not weaken them.

### Settings
- All user-configurable state goes through `Properties.Settings.Default`.
- Call `Properties.Settings.Default.Save()` after mutations; the app does this on close but
  save explicitly after any setting change that must survive a crash.
- Do not add settings without a corresponding UI control in `SettingsPage.xaml`.

### Logging
- `Logger.Log()` — internal info, file only (no toast).
- `Logger.Success()` — user-visible positive outcome (green toast + file).
- `Logger.Warn()` — recoverable problem the user should know about (yellow toast + file).
- `Logger.Error()` — something went wrong (red toast + file).
- Logs rotate daily to `%LOCALAPPDATA%\RemnantSaveGuardian\logs\log_yyyy-MM-dd.txt`.
  After any test run, check the log file to verify the correct code paths fired.

---

## Git workflow

- Work on feature branches; open a PR to `main`.
- `main` is protected — the `build` CI job (restore → build → **test** → publish) must pass
  before merge. Tests failing = no merge.
- Commit messages: imperative mood, short subject, explain *why* not *what*.
- Update `CHANGELOG.md` under `[Unreleased]` for every user-visible change. Promote to a
  version heading on release.
- After merging to `main`, bump `AssemblyVersion` in the `.csproj` and publish.

---

## Things to avoid

- Do not clear the staging folder before a validated replacement is ready — `SyncFromWgs`
  already does this correctly via temp dir + atomic swap.
- Do not return success from `RestoreToWgs` if any `.sav` file could not be mapped — this
  was a previous bug; the current implementation throws and rolls back.
- Do not add business logic to `MainWindow.xaml.cs` — it is already too large.
- Do not hardcode `Documents\` paths for app-managed data — use `AppData\Local`.
- Do not show UI toasts for routine `Logger.Log()` info messages — they were suppressed
  deliberately to avoid startup notification noise.
