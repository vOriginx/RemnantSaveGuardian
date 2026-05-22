using System;
using System.IO;
using System.Linq;
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
}
