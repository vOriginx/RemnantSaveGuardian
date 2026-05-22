# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.4.3] - 2026-05-22

### Added

- **Xbox Game Pass save support** — on first launch the app auto-detects the WGS package
  folder (`AppData\Local\Packages\PerfectWorldEntertainment.GFREMP2_jrajkyc4tsa6w\SystemAppData\wgs`),
  reads `containers.index` to resolve the authoritative name-to-GUID subfolder mapping, and
  copies the saves to a staging folder as human-readable `profile.sav` / `save_N.sav` files.
  No manual folder configuration required.
- **`ParseContainersIndex`** — binary parser for the Xbox `containers.index` format.
  Each entry encodes a container name (UTF-16LE, appears twice), a type string, 5 flag bytes,
  and a 16-byte little-endian GUID that identifies the save subfolder on disk. The parser
  uses a sliding-window scan with duplicate-name verification and falls back to creation-time
  + file-size ordering if the file is absent or unreadable.
- **Live sync via `FileSystemWatcher`** — a watcher on the WGS source folder detects
  in-game saves (any non-`container*` file change) and re-runs the sync after a 2-second
  debounce, triggering an automatic backup without user interaction.
- **`RestoreToWgs`** — writes restored `.sav` files back to their correct WGS GUID files so
  the game sees the change immediately, matching Steam restore behaviour. A `.rsg_bak` safety
  copy is created for every file before it is overwritten; if any write fails the entire
  operation is rolled back and all backup copies are removed.
- **Atomic staging sync** — `SyncFromWgs` now builds the new file set in a temp directory,
  validates that `profile.sav` was produced, then replaces the staging folder contents in one
  step. A transient WGS lock or partial container state no longer leaves the staging folder
  empty.
- **Per-date log files** — `Logger` writes to
  `AppData\Local\RemnantSaveGuardian\logs\log_yyyy-MM-dd.txt`, rotating at midnight.
  Each line is prefixed with timestamp and severity (`INFO / OK / WARN / ERROR`).
  Controlled by the existing **Create log file** setting (default: on).
- **Unit test project** — `RemnantSaveGuardian.Tests` (xUnit, `net8.0-windows`) with 21 tests
  covering `FindWgsSaveFolder`, `SyncFromWgs`, `ParseContainersIndex`, and `RestoreToWgs`
  including atomic-swap and rollback behaviour.
- **CI pipeline** (`dotnet-desktop.yml`) — explicit restore → build → test → publish order;
  tests block publish on failure; `.trx` results uploaded as artifacts.

### Changed

- App-managed data moved from `Documents` to `AppData\Local\RemnantSaveGuardian\{Saves,Backups}`
  — the correct Windows location for data the user does not manage directly.
- Info-level log messages no longer show as UI toast notifications; only Success, Warning, and
  Error are surfaced to the snackbar.
- `CreateLogFile` setting defaults changed to `True` in `Settings.settings`, `App.config`, and
  `Settings.Designer.cs` (previously inconsistent: `Settings.settings` was `True`, the other
  two remained `False`).

### Fixed

- `DefaultSaveFolder()` threw `DirectoryNotFoundException` for Game Pass users because
  `Directory.GetDirectories` was called on `Saved Games\Remnant2` without checking existence
  first. The WGS fallback path never ran.
- `RestoreToWgs` previously returned `true` even when no files were written (unmatched
  containers, missing GUID subfolders, or missing target files were silently skipped).
  It now throws on any unresolvable `.sav` file, triggering rollback, and returns `false`
  if zero files were in the source folder.
- Duplicate `PostBuild` target — MSBuild silently drops the first when two targets share a
  name; the `ZipDirectory` step was never running. Merged into a single target.

### Security / Safety

- `RestoreToWgs` creates a `.rsg_bak` backup of every WGS file before overwriting and rolls
  back all changes on failure, preventing partial writes from corrupting save data.
- `SyncFromWgs` never clears the staging folder until a validated replacement set is ready.
