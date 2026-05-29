using ArkManager.Core.Services.Config;
using Xunit;

namespace ArkManager.Core.Tests.Config;

public class ConfigServiceTests
{
    [Fact]
    public void Snapshot_DefaultsWhenNoIni()
    {
        using var env = new ConfigTestEnv();
        var timer = new FakeDebounceTimer();
        using var svc = new ConfigService(env.Settings, timer);

        Assert.Equal("My ASA Server", svc.Snapshot.SessionName);
        Assert.Equal(7777, svc.Snapshot.Port);
        Assert.Equal(27015, svc.Snapshot.QueryPort);
        Assert.Equal(27020, svc.Snapshot.RconPort);
        Assert.True(svc.Snapshot.RconEnabled);
        Assert.Equal("", svc.Snapshot.ServerPassword);
        Assert.Equal("", svc.Snapshot.AdminPassword);
        Assert.Equal("", svc.Snapshot.SpectatorPassword);
    }

    [Fact]
    public void Snapshot_LoadsFromExistingIni()
    {
        using var env = new ConfigTestEnv();
        env.WriteIni("""
            [ServerSettings]
            ServerPassword=srv-pw
            ServerAdminPassword=adm-pw
            SpectatorPassword=spec-pw
            RCONEnabled=True
            RCONPort=27099
            AllowThirdPersonPlayer=True

            [SessionSettings]
            SessionName=My Test Server
            Port=7799
            QueryPort=27042

            [/Script/Engine.GameSession]
            MaxPlayers=42
            """);
        var timer = new FakeDebounceTimer();
        using var svc = new ConfigService(env.Settings, timer);

        Assert.Equal("My Test Server", svc.Snapshot.SessionName);
        Assert.Equal(7799, svc.Snapshot.Port);
        Assert.Equal(27042, svc.Snapshot.QueryPort);
        Assert.Equal(27099, svc.Snapshot.RconPort);
        Assert.True(svc.Snapshot.RconEnabled);
        Assert.Equal("srv-pw", svc.Snapshot.ServerPassword);
        Assert.Equal("adm-pw", svc.Snapshot.AdminPassword);
        Assert.Equal("spec-pw", svc.Snapshot.SpectatorPassword);
    }

    [Fact]
    public void UpdateBasic_WritesIniWithMutatedValues()
    {
        using var env = new ConfigTestEnv();
        var timer = new FakeDebounceTimer();
        using var svc = new ConfigService(env.Settings, timer);

        svc.UpdateBasic(b => { b.RconPort = 27050; b.AdminPassword = "newpw"; });

        Assert.True(File.Exists(env.GameUserSettingsPath));
        var contents = env.ReadIni();
        Assert.Contains("RCONPort=27050", contents);
        Assert.Contains("ServerAdminPassword=newpw", contents);
    }

    [Fact]
    public void UpdateBasic_PreservesUnknownKeys()
    {
        using var env = new ConfigTestEnv();
        env.WriteIni("""
            [ServerSettings]
            ServerPassword=keep-me
            AllowThirdPersonPlayer=True
            TheMaxStructuresInRange=10500

            [Custom]
            WhateverASAWrote=yes
            """);
        var timer = new FakeDebounceTimer();
        using var svc = new ConfigService(env.Settings, timer);

        svc.UpdateBasic(b => b.RconPort = 27050);

        var contents = env.ReadIni();
        Assert.Contains("ServerPassword=keep-me", contents);
        Assert.Contains("AllowThirdPersonPlayer=True", contents);
        Assert.Contains("TheMaxStructuresInRange=10500", contents);
        Assert.Contains("[Custom]", contents);
        Assert.Contains("WhateverASAWrote=yes", contents);
        Assert.Contains("RCONPort=27050", contents);
    }

    [Fact]
    public void UpdateBasic_RaisesPropertyChangedOnlyForChanged()
    {
        using var env = new ConfigTestEnv();
        var timer = new FakeDebounceTimer();
        using var svc = new ConfigService(env.Settings, timer);

        var changed = new List<string>();
        svc.Snapshot.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");

        svc.UpdateBasic(b => b.SessionName = "NewName");

        Assert.Single(changed);
        Assert.Equal(nameof(ServerConfigSnapshot.SessionName), changed[0]);
    }
}
