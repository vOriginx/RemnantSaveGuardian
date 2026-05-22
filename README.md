# Remnant Save Guardian
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

Once the user folder is found, the app collects every file in every subdirectory whose name does **not** start with `container`. It then sorts these files by two criteria:

1. **Subdirectory creation time** (ascending) — the oldest subdirectory was created first, which corresponds to the character profile
2. **File size** (ascending, as a tiebreaker for same-age directories)

The sorted files are renamed in order:

| Sort position | Output name |
|---|---|
| 0 (oldest/smallest) | `profile.sav` |
| 1 | `save_0.sav` |
| 2 | `save_1.sav` |
| … | … |

These renamed copies are written to `Documents\RSG_Saves\`, which becomes the app's active save folder. The originals in the WGS package folder are never modified.

### Live sync during gameplay

A file system watcher monitors the WGS source folder while the app is running. When the game writes new save data (detected as any non-`container` file change in the WGS subdirectories), the app waits two seconds for the write to settle and then re-runs the sync automatically. This means backups trigger in response to in-game saves without any manual steps.

## Known Issues
- [Some items are missing from the world analyzer](https://github.com/Razzmatazzz/RemnantSaveGuardian/issues/43)
- [Many events and items do not have well-formatted names](https://github.com/Razzmatazzz/RemnantSaveGuardian/issues/45)
- [Some events not displaying or are erroneously displaying](https://github.com/Razzmatazzz/RemnantSaveGuardian/issues/44)
- [If you are using Norton Antivirus, it may cause weirdness with your game saves and RemnantSaveGuardian](https://github.com/Razzmatazzz/RemnantSaveGuardian/issues/70)

Thanks to [crackedmind](https://github.com/crackedmind) for the inflation code to convert saves into partial plaintext.

Thanks to [AuriCrystal](https://github.com/Auricrystal) for event/item list.
