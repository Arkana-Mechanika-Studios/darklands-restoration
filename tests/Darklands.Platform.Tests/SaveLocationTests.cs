using System.Security.AccessControl;
using System.Security.Principal;

namespace Darklands.Platform.Tests;

public class SaveLocationTests
{
    [Fact]
    public void A_configured_folder_is_used_as_given()
    {
        var tested = new List<string>();
        var choice = SaveLocation.Choose("D:/MySaves", "C:/Game/SAVES", "C:/User/SAVES", f => { tested.Add(f); return false; });
        Assert.Equal(new SaveLocationChoice("D:/MySaves", false), choice);
        Assert.Empty(tested);
    }

    [Fact]
    public void The_game_folder_is_used_while_it_can_be_written()
    {
        Assert.Equal(new SaveLocationChoice("C:/Game/SAVES", false), SaveLocation.Choose(null, "C:/Game/SAVES", "C:/User/SAVES", _ => true));
    }

    [Fact]
    public void An_unwritable_game_folder_falls_back_to_the_user_folder()
    {
        Assert.Equal(new SaveLocationChoice("C:/User/SAVES", true), SaveLocation.Choose(null, "C:/Game/SAVES", "C:/User/SAVES", _ => false));
    }

    [Fact]
    public void The_user_folder_is_beside_the_per_user_settings_file()
    {
        Assert.Equal(Path.Combine("C:/AppData", "DarklandsRestoration", "SAVES"), SaveLocation.UserFolder("C:/AppData"));
        Assert.Equal(Path.GetDirectoryName(SettingsFile.SearchPaths("C:/Program", "C:/AppData")[1]),
            Path.GetDirectoryName(SaveLocation.UserFolder("C:/AppData")));
    }

    [Fact]
    public void A_writable_folder_passes_the_probe_and_keeps_nothing()
    {
        using var scratch = new Scratch();
        var saves = scratch.Folder("SAVES");
        Assert.True(SaveLocation.CanWrite(saves));
        Assert.Empty(Directory.EnumerateFileSystemEntries(saves));
    }

    [Fact]
    public void A_missing_folder_is_probed_in_its_parent_and_not_created()
    {
        using var scratch = new Scratch();
        var game = scratch.Folder("game");
        var saves = Path.Combine(game, "SAVES");
        Assert.True(SaveLocation.CanWrite(saves));
        Assert.False(Directory.Exists(saves));
        Assert.Empty(Directory.EnumerateFileSystemEntries(game));
    }

    [Fact]
    public void A_folder_denying_writes_fails_the_probe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        using var scratch = new Scratch();
        var saves = new DirectoryInfo(scratch.Folder("SAVES"));
        var me = WindowsIdentity.GetCurrent().User!;
        var deny = new FileSystemAccessRule(me, FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories, AccessControlType.Deny);
        var acl = saves.GetAccessControl();
        acl.AddAccessRule(deny);
        saves.SetAccessControl(acl);
        try
        {
            Assert.False(SaveLocation.CanWrite(saves.FullName));
            Assert.False(SaveLocation.CanWrite(Path.Combine(saves.FullName, "sub")));
        }
        finally
        {
            acl.RemoveAccessRule(deny);
            saves.SetAccessControl(acl);
        }
    }
}
