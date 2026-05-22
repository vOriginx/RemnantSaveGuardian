using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace RemnantSaveGuardian.Tests;

public class WgsSyncTests : IDisposable
{
    private readonly string _root;

    public WgsSyncTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"RSGTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>
    /// Builds a minimal containers.index binary matching the WGS format:
    /// per entry: [uint32 nameLen][UTF-16LE name][uint32 nameLen][UTF-16LE name][uint32 typeLen][UTF-16LE type][5 flags bytes][16-byte GUID]
    /// </summary>
    private static byte[] BuildContainersIndex(params (string name, string type, string guidN)[] entries)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        // Dummy header bytes so the scanner has to search, not just read from offset 0
        w.Write(new byte[] { 0x01, 0x00, 0x00, 0x00, 0x05 });

        foreach (var (name, type, guidN) in entries)
        {
            var nameBytes = Encoding.Unicode.GetBytes(name);
            var typeBytes = Encoding.Unicode.GetBytes(type);
            var guidBytes = Guid.ParseExact(guidN, "N").ToByteArray();

            w.Write(name.Length);
            w.Write(nameBytes);
            w.Write(name.Length);
            w.Write(nameBytes);
            w.Write(type.Length);
            w.Write(typeBytes);
            w.Write(new byte[5]); // flags
            w.Write(guidBytes);
        }

        return ms.ToArray();
    }

    private static string GuidToFolderName(string guidN) =>
        new Guid(Guid.ParseExact(guidN, "N").ToByteArray()).ToString("N").ToUpperInvariant();

    // ── FindWgsSaveFolder ────────────────────────────────────────────────────

    [Fact]
    public void FindWgsSaveFolder_ReturnsNull_WhenRootDoesNotExist()
    {
        var result = RemnantSave.FindWgsSaveFolder(Path.Combine(_root, "nonexistent"));
        Assert.Null(result);
    }

    [Fact]
    public void FindWgsSaveFolder_ReturnsNull_WhenNoSubfolderHasContainersIndex()
    {
        Directory.CreateDirectory(Path.Combine(_root, "userFolder"));
        Directory.CreateDirectory(Path.Combine(_root, "t"));

        var result = RemnantSave.FindWgsSaveFolder(_root);
        Assert.Null(result);
    }

    [Fact]
    public void FindWgsSaveFolder_ReturnsFolder_WhenContainersIndexPresent()
    {
        var userFolder = Directory.CreateDirectory(Path.Combine(_root, "0009000000E10929_ABC")).FullName;
        File.WriteAllText(Path.Combine(userFolder, "containers.index"), "");

        var result = RemnantSave.FindWgsSaveFolder(_root);
        Assert.Equal(userFolder, result);
    }

    [Fact]
    public void FindWgsSaveFolder_SkipsFolders_WithoutContainersIndex()
    {
        Directory.CreateDirectory(Path.Combine(_root, "t"));
        var userFolder = Directory.CreateDirectory(Path.Combine(_root, "0009000000E10929_ABC")).FullName;
        File.WriteAllText(Path.Combine(userFolder, "containers.index"), "");

        var result = RemnantSave.FindWgsSaveFolder(_root);
        Assert.Equal(userFolder, result);
    }

    // ── SyncFromWgs ──────────────────────────────────────────────────────────

    [Fact]
    public void SyncFromWgs_CreatesDestFolder_WhenItDoesNotExist()
    {
        var src = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;
        var dest = Path.Combine(_root, "dest");

        RemnantSave.SyncFromWgs(src, dest);

        Assert.True(Directory.Exists(dest));
    }

    [Fact]
    public void SyncFromWgs_ClearsExistingFilesFromDest()
    {
        var src = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;
        var dest = Directory.CreateDirectory(Path.Combine(_root, "dest")).FullName;
        File.WriteAllText(Path.Combine(dest, "stale.sav"), "old");

        RemnantSave.SyncFromWgs(src, dest);

        Assert.Empty(Directory.GetFiles(dest));
    }

    [Fact]
    public void SyncFromWgs_SkipsContainerFiles()
    {
        var src = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;
        var subdir = Directory.CreateDirectory(Path.Combine(src, "saveslot")).FullName;
        File.WriteAllText(Path.Combine(subdir, "container.1"), "metadata");
        File.WriteAllText(Path.Combine(subdir, "ABCDEF1234567890"), "savedata");
        var dest = Path.Combine(_root, "dest");

        RemnantSave.SyncFromWgs(src, dest);

        var files = Directory.GetFiles(dest).Select(Path.GetFileName).ToArray();
        Assert.DoesNotContain("container.1", files);
        Assert.Single(files);
    }

    [Fact]
    public void SyncFromWgs_NamesFirstFile_ProfileSav()
    {
        var src = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;
        var subdir = Directory.CreateDirectory(Path.Combine(src, "saveslot")).FullName;
        File.WriteAllText(Path.Combine(subdir, "GUID0000000000001"), "data");
        var dest = Path.Combine(_root, "dest");

        RemnantSave.SyncFromWgs(src, dest);

        Assert.True(File.Exists(Path.Combine(dest, "profile.sav")));
    }

    [Fact]
    public void SyncFromWgs_NamesAdditionalFiles_SaveN()
    {
        var src = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;

        // Create two subdirs with a small time gap so ordering is deterministic
        var slot0 = Directory.CreateDirectory(Path.Combine(src, "slot0")).FullName;
        File.WriteAllText(Path.Combine(slot0, "GUID000000000001"), "profile data");
        Thread.Sleep(10);
        var slot1 = Directory.CreateDirectory(Path.Combine(src, "slot1")).FullName;
        File.WriteAllText(Path.Combine(slot1, "GUID000000000002"), "world data");

        var dest = Path.Combine(_root, "dest");
        RemnantSave.SyncFromWgs(src, dest);

        Assert.True(File.Exists(Path.Combine(dest, "profile.sav")));
        Assert.True(File.Exists(Path.Combine(dest, "save_0.sav")));
    }

    [Fact]
    public void SyncFromWgs_OrdersByFolderCreationTimeThenSize()
    {
        var src = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;

        // Oldest folder gets profile.sav regardless of file name ordering
        var older = Directory.CreateDirectory(Path.Combine(src, "older")).FullName;
        File.WriteAllText(Path.Combine(older, "ZZZZZZ"), "profile");
        Thread.Sleep(20);
        var newer = Directory.CreateDirectory(Path.Combine(src, "newer")).FullName;
        File.WriteAllText(Path.Combine(newer, "AAAAAA"), "world");

        var dest = Path.Combine(_root, "dest");
        RemnantSave.SyncFromWgs(src, dest);

        Assert.Equal("profile", File.ReadAllText(Path.Combine(dest, "profile.sav")));
        Assert.Equal("world", File.ReadAllText(Path.Combine(dest, "save_0.sav")));
    }

    [Fact]
    public void SyncFromWgs_HandlesEmptySourceGracefully()
    {
        var src = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;
        var dest = Path.Combine(_root, "dest");

        var ex = Record.Exception(() => RemnantSave.SyncFromWgs(src, dest));

        Assert.Null(ex);
        Assert.Empty(Directory.GetFiles(dest));
    }

    [Fact]
    public void SyncFromWgs_MultipleWorldSaves_AreNumberedSequentially()
    {
        var src = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;

        for (int i = 0; i < 4; i++)
        {
            Thread.Sleep(10);
            var slot = Directory.CreateDirectory(Path.Combine(src, $"slot{i}")).FullName;
            File.WriteAllText(Path.Combine(slot, $"GUID{i:D16}"), $"data{i}");
        }

        var dest = Path.Combine(_root, "dest");
        RemnantSave.SyncFromWgs(src, dest);

        Assert.True(File.Exists(Path.Combine(dest, "profile.sav")));
        Assert.True(File.Exists(Path.Combine(dest, "save_0.sav")));
        Assert.True(File.Exists(Path.Combine(dest, "save_1.sav")));
        Assert.True(File.Exists(Path.Combine(dest, "save_2.sav")));
        Assert.Equal(4, Directory.GetFiles(dest).Length);
    }

    // ── ParseContainersIndex ─────────────────────────────────────────────────

    [Fact]
    public void ParseContainersIndex_ReturnsEmpty_WhenFileNotFound()
    {
        var result = RemnantSave.ParseContainersIndex(Path.Combine(_root, "nonexistent"));
        Assert.Empty(result);
    }

    [Fact]
    public void ParseContainersIndex_ReturnsEmpty_ForEmptyFile()
    {
        var indexPath = Path.Combine(_root, "containers.index");
        File.WriteAllBytes(indexPath, Array.Empty<byte>());
        Assert.Empty(RemnantSave.ParseContainersIndex(indexPath));
    }

    [Fact]
    public void ParseContainersIndex_ReturnsSingleMapping()
    {
        const string profileGuid = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var data = BuildContainersIndex(("profile", "GameSave", profileGuid));
        var indexPath = Path.Combine(_root, "containers.index");
        File.WriteAllBytes(indexPath, data);

        var result = RemnantSave.ParseContainersIndex(indexPath);

        Assert.Single(result);
        Assert.Equal(GuidToFolderName(profileGuid), result["profile"]);
    }

    [Fact]
    public void ParseContainersIndex_ReturnsAllMappings_ForMultipleEntries()
    {
        const string profileGuid = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        const string save0Guid   = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        const string save1Guid   = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";
        var data = BuildContainersIndex(
            ("profile", "GameSave", profileGuid),
            ("save_0",  "GameSave", save0Guid),
            ("save_1",  "GameSave", save1Guid));
        var indexPath = Path.Combine(_root, "containers.index");
        File.WriteAllBytes(indexPath, data);

        var result = RemnantSave.ParseContainersIndex(indexPath);

        Assert.Equal(3, result.Count);
        Assert.Equal(GuidToFolderName(profileGuid), result["profile"]);
        Assert.Equal(GuidToFolderName(save0Guid),   result["save_0"]);
        Assert.Equal(GuidToFolderName(save1Guid),   result["save_1"]);
    }

    [Fact]
    public void ParseContainersIndex_IsCaseInsensitive_ForLookup()
    {
        const string profileGuid = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var data = BuildContainersIndex(("profile", "GameSave", profileGuid));
        var indexPath = Path.Combine(_root, "containers.index");
        File.WriteAllBytes(indexPath, data);

        var result = RemnantSave.ParseContainersIndex(indexPath);

        Assert.True(result.ContainsKey("PROFILE"));
        Assert.True(result.ContainsKey("Profile"));
    }

    // ── SyncFromWgs with containers.index ────────────────────────────────────

    [Fact]
    public void SyncFromWgs_UsesContainersIndex_WhenPresent()
    {
        const string profileGuid = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        const string save0Guid   = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        var profileFolder = GuidToFolderName(profileGuid);
        var save0Folder   = GuidToFolderName(save0Guid);

        var src = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;

        // GUID subfolders with actual save data files
        var pd = Directory.CreateDirectory(Path.Combine(src, profileFolder)).FullName;
        File.WriteAllText(Path.Combine(pd, "container.1"), "meta");
        File.WriteAllText(Path.Combine(pd, "PROFILEFILE00001"), "profile data");

        var sd = Directory.CreateDirectory(Path.Combine(src, save0Folder)).FullName;
        File.WriteAllText(Path.Combine(sd, "container.1"), "meta");
        File.WriteAllText(Path.Combine(sd, "SAVE0FILE000001"), "world data");

        File.WriteAllBytes(Path.Combine(src, "containers.index"),
            BuildContainersIndex(
                ("profile", "GameSave", profileGuid),
                ("save_0",  "GameSave", save0Guid)));

        var dest = Path.Combine(_root, "dest");
        RemnantSave.SyncFromWgs(src, dest);

        Assert.Equal("profile data", File.ReadAllText(Path.Combine(dest, "profile.sav")));
        Assert.Equal("world data",   File.ReadAllText(Path.Combine(dest, "save_0.sav")));
    }

    // ── RestoreToWgs ─────────────────────────────────────────────────────────

    [Fact]
    public void RestoreToWgs_ReturnsFalse_WhenNoContainersIndex()
    {
        var src = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;
        var wgs = Directory.CreateDirectory(Path.Combine(_root, "wgs")).FullName;
        File.WriteAllText(Path.Combine(src, "profile.sav"), "data");

        Assert.False(RemnantSave.RestoreToWgs(src, wgs));
    }

    [Fact]
    public void RestoreToWgs_WritesBackToGuidFiles()
    {
        const string profileGuid = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var folderName = GuidToFolderName(profileGuid);
        var wgs = Directory.CreateDirectory(Path.Combine(_root, "wgs")).FullName;

        // Set up WGS structure
        var guidDir = Directory.CreateDirectory(Path.Combine(wgs, folderName)).FullName;
        File.WriteAllText(Path.Combine(guidDir, "container.1"), "meta");
        var targetFile = Path.Combine(guidDir, "PROFILEGUIDFILE1");
        File.WriteAllText(targetFile, "old save data");

        File.WriteAllBytes(Path.Combine(wgs, "containers.index"),
            BuildContainersIndex(("profile", "GameSave", profileGuid)));

        // Source staging folder with restored backup
        var src = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;
        File.WriteAllText(Path.Combine(src, "profile.sav"), "restored data");

        var ok = RemnantSave.RestoreToWgs(src, wgs);

        Assert.True(ok);
        Assert.Equal("restored data", File.ReadAllText(targetFile));
    }

    [Fact]
    public void RestoreToWgs_CleansUpBackupFiles_OnSuccess()
    {
        const string profileGuid = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var folderName = GuidToFolderName(profileGuid);
        var wgs = Directory.CreateDirectory(Path.Combine(_root, "wgs")).FullName;

        var guidDir = Directory.CreateDirectory(Path.Combine(wgs, folderName)).FullName;
        File.WriteAllText(Path.Combine(guidDir, "container.1"), "meta");
        File.WriteAllText(Path.Combine(guidDir, "PROFILEGUIDFILE1"), "old");

        File.WriteAllBytes(Path.Combine(wgs, "containers.index"),
            BuildContainersIndex(("profile", "GameSave", profileGuid)));

        var src = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;
        File.WriteAllText(Path.Combine(src, "profile.sav"), "new");

        RemnantSave.RestoreToWgs(src, wgs);

        Assert.Empty(Directory.GetFiles(guidDir, "*.rsg_bak"));
    }
}
