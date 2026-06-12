using System;
using ArkManager.Core.Services.Backups;
using Xunit;

namespace ArkManager.Core.Tests;

public class BackupInfoTests
{
    // The note is encoded into the file name as `asa-backup-{stamp}_{safeNote}.zip`.
    // ListBackups must read it back, otherwise every snapshot shows up as "Auto snapshot".
    [Theory]
    [InlineData("asa-backup-20260612-171600.zip", null)]                 // manual, no note
    [InlineData("asa-backup-20260612-171600_auto.zip", "auto")]          // auto snapshot
    [InlineData("asa-backup-20260612-171600_beforeraid.zip", "beforeraid")]
    [InlineData("asa-backup-20260612-171600_my_backup.zip", "my_backup")] // note with underscores
    [InlineData("asa-backup-20260612-171600_pre-restore-auto.zip", "pre-restore-auto")]
    public void NoteFromFileName_RoundTripsSuffix(string fileName, string? expected)
    {
        Assert.Equal(expected, BackupService.NoteFromFileName(fileName));
    }

    [Fact]
    public void DisplayName_NoNote_IsManual()
    {
        var info = new BackupInfo("/x.zip", DateTime.UtcNow, 0, null);
        Assert.Equal("Manual snapshot", info.DisplayName);
    }

    [Fact]
    public void DisplayName_AutoNote_IsAutoSnapshot()
    {
        var info = new BackupInfo("/x.zip", DateTime.UtcNow, 0, BackupService.AutoNote);
        Assert.Equal("Auto snapshot", info.DisplayName);
    }

    [Fact]
    public void DisplayName_PreRestoreNote_IsPreRestore()
    {
        var info = new BackupInfo("/x.zip", DateTime.UtcNow, 0, BackupService.PreRestoreNote);
        Assert.Equal("Pre-restore snapshot", info.DisplayName);
    }

    [Fact]
    public void DisplayName_UserNote_ShowsNote()
    {
        var info = new BackupInfo("/x.zip", DateTime.UtcNow, 0, "beforeraid");
        Assert.Equal("beforeraid", info.DisplayName);
    }
}
