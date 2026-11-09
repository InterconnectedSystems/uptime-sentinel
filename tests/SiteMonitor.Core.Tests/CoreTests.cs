using SiteMonitor.Core.Alerting;
using SiteMonitor.Core.Checks;
using SiteMonitor.Core.Config;
using SiteMonitor.Core.Models;

namespace SiteMonitor.Core.Tests;

public class CheckFactoryTests
{
    [Theory]
    [InlineData("https://example.com", "HTTP", "https://example.com/")]
    [InlineData("http://10.0.0.5:8080/health", "HTTP", "http://10.0.0.5:8080/health")]
    [InlineData("example.com", "Ping/TCP:443/TCP:80", "example.com")]
    [InlineData("192.168.1.1", "Ping/TCP:443/TCP:80", "192.168.1.1")]
    [InlineData("  server01  ", "Ping/TCP:443/TCP:80", "server01")]
    public void Auto_mode_picks_sensible_check(string address, string kind, string endpoint)
    {
        var rc = CheckFactory.Create(new MonitorTarget { Address = address });
        Assert.Equal(kind, rc.Check.Kind);
        Assert.Equal(endpoint, rc.Endpoint);
    }

    [Fact]
    public void Auto_with_port_uses_tcp()
    {
        var rc = CheckFactory.Create(new MonitorTarget { Address = "db.local", Port = 1433 });
        Assert.Equal("TCP:1433", rc.Check.Kind);
    }

    [Fact]
    public void Explicit_ping_strips_url_parts()
    {
        var rc = CheckFactory.Create(new MonitorTarget { Address = "https://example.com/path", CheckType = CheckType.Ping });
        Assert.Equal("example.com", rc.Endpoint);
    }

    [Fact]
    public void Explicit_http_adds_scheme()
    {
        var rc = CheckFactory.Create(new MonitorTarget { Address = "example.com", CheckType = CheckType.Http });
        Assert.Equal("http://example.com", rc.Endpoint);
    }

    [Fact]
    public void Validation_enforces_minimum_interval_and_address()
    {
        Assert.NotEmpty(new MonitorTarget { Address = "" }.Validate());
        Assert.NotEmpty(new MonitorTarget { Address = "x", IntervalSeconds = 5 }.Validate());
        Assert.NotEmpty(new MonitorTarget { Address = "x", CheckType = CheckType.Tcp }.Validate());
        Assert.Empty(new MonitorTarget { Address = "x", IntervalSeconds = 10 }.Validate());
    }

    [Fact]
    public void Effective_values_fall_back_to_global()
    {
        var g = new GlobalSettings { DefaultIntervalSeconds = 120, DefaultFailThreshold = 5 };
        var t = new MonitorTarget { Address = "x" };
        Assert.Equal(120, t.EffectiveInterval(g));
        Assert.Equal(5, t.EffectiveThreshold(g));
        t.IntervalSeconds = 30;
        Assert.Equal(30, t.EffectiveInterval(g));
    }
}

public class ConfigAndSmtpTests
{
    [Fact]
    public void Config_round_trips_through_json()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sm-test-" + Guid.NewGuid());
        try
        {
            var store = new ConfigStore(dir);
            var cfg = new AppConfig();
            cfg.Global.DefaultFailThreshold = 4;
            cfg.Smtp = new SmtpSettings { Host = "smtp.test", Port = 465, Security = SmtpSecurity.SslOnConnect, From = "a@b.c", To = "x@y.z; q@r.s", ProtectedPassword = SecretProtector.Protect("hunter2") };
            cfg.Targets.Add(new MonitorTarget { Name = "Site", Address = "https://example.com", CheckType = CheckType.Http, Keyword = "Example", IntervalSeconds = 30 });
            store.SaveConfig(cfg);

            var loaded = store.LoadConfig();
            Assert.Equal(4, loaded.Global.DefaultFailThreshold);
            Assert.Equal(SmtpSecurity.SslOnConnect, loaded.Smtp.Security);
            Assert.Equal(new[] { "x@y.z", "q@r.s" }, loaded.Smtp.Recipients());
            Assert.Equal("hunter2", SecretProtector.Unprotect(loaded.Smtp.ProtectedPassword));
            var t = Assert.Single(loaded.Targets);
            Assert.Equal(cfg.Targets[0].Id, t.Id);
            Assert.Equal("Example", t.Keyword);
            Assert.Contains("\"checkType\": \"Http\"", File.ReadAllText(store.ConfigPath));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Missing_config_gives_defaults()
    {
        var store = new ConfigStore(Path.Combine(Path.GetTempPath(), "sm-none-" + Guid.NewGuid()));
        var cfg = store.LoadConfig();
        Assert.Equal(3, cfg.Global.DefaultFailThreshold);
        Assert.Empty(cfg.Targets);
    }

    [Fact]
    public void Presets_are_well_formed_and_apply()
    {
        Assert.Equal(SmtpPresets.CustomName, SmtpPresets.All[0].Name);
        Assert.Equal(SmtpPresets.All.Count, SmtpPresets.All.Select(p => p.Name).Distinct().Count());
        foreach (var p in SmtpPresets.All.Skip(1))
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Host));
            Assert.StartsWith("https://", p.SignupUrl);
            Assert.True(p.Security == SmtpSecurity.SslOnConnect ? p.Port == 465 : p.Port == 587);
        }

        var s = new SmtpSettings { Username = "keep", From = "keep@x.y" };
        SmtpPresets.Apply(SmtpPresets.Find("Brevo (free tier)")!, s);
        Assert.Equal("smtp-relay.brevo.com", s.Host);
        Assert.Equal("keep", s.Username);
    }
}
