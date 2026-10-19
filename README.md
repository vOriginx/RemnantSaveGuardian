# Remnant Save Guardian

[![CI](https://github.com/vOriginx/RemnantSaveGuardian/actions/workflows/dotnet-desktop.yml/badge.svg)](https://github.com/vOriginx/RemnantSaveGuardian/actions/workflows/dotnet-desktop.yml)

Back up your Remnant 2 saves and view your world rolls.

**This project is a work in progress. Lots of features are broken or partially implemented.**

## Installation
1. Download and install [Microsoft .NET 8.0](https://dotnet.microsoft.com/en-us/download) or greater
2. Download the [latest release](https://github.com/vOriginx/RemnantSaveGuardian/releases/latest/)
3. Unzip the latest release to a folder of your choosing (probably not the same folder where you have the game installed)
4. Run RemnantSaveGuardian.exe

### Xbox Game Pass
No additional setup is required. On first launch the app automatically detects and configures your Game Pass saves — see [Xbox Game Pass save support](#xbox-game-pass-save-support) below for details.

## Screenshots
![image](https://github.com/Razzmatazzz/RemnantSaveGuardian/assets/35779878/cc428b0b-7573-4128-a2ae-02ef25ebda36)
![image](https://github.com/Razzmatazzz/RemnantSaveGuardian/assets/35779878/48fc0eae-fc87-47be-bea3-89af08f102a6)
![image](https://github.com/Razzmatazzz/RemnantSaveGuardian/assets/35779878/45d46b50-f3d2-4341-847e-ae3dd4f6df2c)

## Xbox Game Pass save support

Remnant 2 purchased through Xbox Game Pass stores saves in the Windows Gaming Save (WGS) system rather than a standard file path. This section explains how the app finds and works with those saves automatically.

### Where Game Pass saves live

Xbox Game Pass games store save data under a locked package folder:

```
%LOCALAPPDATA%\Packages\PerfectWorldEntertainment.GFREMP2_jrajkyc4tsa6w\SystemAppData\wgs\
```

Inside `wgs\` there is one folder per Xbox Live user account (named with the user's numeric ID), and inside that a flat set of subdirectories — one per save slot. Each subdirectory contains:

| File | Purpose |
|---|---|
| `container.1` (or similar) | WGS metadata — not a save file |
| `<32-char GUID>` | The actual save data (no extension) |

There are no human-readable file names. The game writes each save slot — the character profile and each world save — into its own separate subdirectory with a randomly generated GUID as the filename.

### How the app discovers saves

On startup the app scans `wgs\` for a subdirectory containing a `containers.index` file. That file is written by the Xbox runtime when the package is active and marks the correct user folder.

```
wgs\
  0009000000E10929_…\        ← located by finding containers.index here
    containers.index
    A3F2…\
      container.1
      D4B7C2E1F0A39856…      ← profile save data
    8C01…\
      container.1
      91A4E3D2B5C78F60…      ← world save 0
    …
```

### How GUID files are mapped to named saves

The app reads `containers.index` to get the authoritative mapping from container name to GUID subfolder. This file is maintained by the Xbox runtime and explicitly records which subfolder holds each save slot.

The binary format of each entry inside `containers.index` is:

```
[uint32 nameLen] [UTF-16LE name] [uint32 nameLen] [UTF-16LE name] [uint32 typeLen] [UTF-16LE type] [5 flag bytes] [16-byte binary GUID]
```

The name appears twice consecutively; the 16-byte GUID decoded as `new Guid(bytes).ToString("N").ToUpperInvariant()` gives the exact subfolder name on disk. For example:

| Container name | GUID subfolder (example) | Output name |
|---|---|---|
| `profile` | `ADE5F93C4FE04C51AE772A0BB76AB817` | `profile.sav` |
| `save_0` | `84B24D100ABB4FD498F107044095FE9B` | `save_0.sav` |
| `save_1` | `CA8B5811BEFF428FA1D3FD17E8966D12` | `save_1.sav` |

Inside each GUID subfolder, the app finds the one file whose name does **not** start with `container` — that is the actual save data. The renamed copies are written to `%LOCALAPPDATA%\RemnantSaveGuardian\Saves\`, which becomes the app's active save folder.

If `containers.index` is absent or cannot be parsed, the app falls back to sorting by subdirectory creation time (oldest = profile) with file size as a tiebreaker.

### Live sync during gameplay

A file system watcher monitors the WGS source folder while the app is running. When the game writes new save data (detected as any non-`container` file change in the WGS subdirectories), the app waits two seconds for the write to settle and then re-runs the sync automatically. This means backups trigger in response to in-game saves without any manual steps.

### Restoring a backup

When you restore a backup, the app writes the `.sav` files back to the WGS GUID files so the game picks them up immediately. Before overwriting each file the app creates a `.rsg_bak` safety copy alongside it; if any write fails, all already-overwritten files are automatically rolled back. The `.rsg_bak` files are deleted once the restore completes successfully.

## Known Issues
- [Some items are missing from the world analyzer](https://github.com/Razzmatazzz/RemnantSaveGuardian/issues/43)
- [Many events and items do not have well-formatted names](https://github.com/Razzmatazzz/RemnantSaveGuardian/issues/45)
- [Some events not displaying or are erroneously displaying](https://github.com/Razzmatazzz/RemnantSaveGuardian/issues/44)
- [If you are using Norton Antivirus, it may cause weirdness with your game saves and RemnantSaveGuardian](https://github.com/Razzmatazzz/RemnantSaveGuardian/issues/70)

Thanks to [crackedmind](https://github.com/crackedmind) for the inflation code to convert saves into partial plaintext.

Thanks to [AuriCrystal](https://github.com/Auricrystal) for event/item list.
