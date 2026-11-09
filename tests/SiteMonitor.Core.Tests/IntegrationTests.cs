using System.Net;
using System.Net.Sockets;
using System.Text;
using SiteMonitor.Core.Alerting;
using SiteMonitor.Core.Checks;
using SiteMonitor.Core.Models;

namespace SiteMonitor.Core.Tests;

/// <summary>Runs real checks/email against local listeners (no internet required).</summary>
public class IntegrationTests
{
    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public async Task Tcp_check_detects_open_and_closed_ports()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int open = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            Assert.True((await new TcpCheck(open).RunAsync("127.0.0.1", 2000, default)).Success);
        }
        finally { listener.Stop(); }

        var closed = await new TcpCheck(FreePort()).RunAsync("127.0.0.1", 2000, default);
        Assert.False(closed.Success);
    }

    [Fact]
    public async Task Tcp_check_times_out_on_unroutable_address()
    {
        var r = await new TcpCheck(80).RunAsync("10.255.255.1", 1000, default);
        Assert.False(r.Success);
    }

    [Fact]
    public async Task Http_check_status_and_keyword()
    {
        int port = FreePort();
        using var http = new HttpListener();
        http.Prefixes.Add($"http://127.0.0.1:{port}/");
        http.Start();
        _ = Task.Run(async () =>
        {
            while (http.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await http.GetContextAsync(); } catch { return; }
                var path = ctx.Request.Url!.AbsolutePath;
                ctx.Response.StatusCode = path == "/broken" ? 500 : 200;
                var bytes = Encoding.UTF8.GetBytes(path == "/broken" ? "error" : "<html>All systems operational</html>");
                if (ctx.Request.HttpMethod != "HEAD") await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        });

        var baseUrl = $"http://127.0.0.1:{port}";
        Assert.True((await new HttpCheck(null, null).RunAsync(baseUrl + "/", 3000, default)).Success);
        Assert.True((await new HttpCheck(null, "operational").RunAsync(baseUrl + "/", 3000, default)).Success);

        var noKeyword = await new HttpCheck(null, "maintenance").RunAsync(baseUrl + "/", 3000, default);
        Assert.False(noKeyword.Success);
        Assert.Contains("keyword", noKeyword.Message);

        var broken = await new HttpCheck(null, null).RunAsync(baseUrl + "/broken", 3000, default);
        Assert.False(broken.Success);
        Assert.Contains("500", broken.Message);

        Assert.False((await new HttpCheck(201, null).RunAsync(baseUrl + "/", 3000, default)).Success);
        http.Stop();
    }

    [Fact]
    public async Task Http_check_reports_connection_refused()
    {
        var r = await new HttpCheck(null, null).RunAsync($"http://127.0.0.1:{FreePort()}/", 3000, default);
        Assert.False(r.Success);
    }

    [Fact]
    public async Task Email_is_delivered_to_smtp_server()
    {
        var server = new FakeSmtpServer();
        var settings = new SmtpSettings
        {
            Host = "127.0.0.1", Port = server.Port, Security = SmtpSecurity.None,
            From = "monitor@test.local", To = "admin@test.local, ops@test.local",
        };

        var sm = new AlertStateMachine(new MonitorTarget { Name = "Shop", Address = "https://shop.test" });
        for (int i = 0; i < 3; i++) sm.Process(CheckResult.Fail("HTTP timed out"), 3, new GlobalSettings());
        var (subject, body) = sm.BuildEmail(AlertKind.Down, new MonitorTarget { Name = "Shop", Address = "https://shop.test" }, "HTTP", 3);

        await EmailNotifier.SendAsync(settings, subject, body, default);
        var data = await server.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains("RCPT TO:<admin@test.local>", server.Commands);
        Assert.Contains("RCPT TO:<ops@test.local>", server.Commands);
        Assert.Contains("Subject: [DOWN] Shop is not responding", data);
        Assert.Contains("HTTP timed out", data);
    }

    /// <summary>Just enough SMTP for MailKit to deliver one message.</summary>
    private sealed class FakeSmtpServer
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        public int Port { get; }
        public List<string> Commands { get; } = new();
        public TaskCompletionSource<string> Received { get; } = new();

        public FakeSmtpServer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(Serve);
        }

        private async Task Serve()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 fake ESMTP");
            while (await reader.ReadLineAsync() is { } line)
            {
                Commands.Add(line);
                var cmd = line.Split(' ')[0].ToUpperInvariant();
                switch (cmd)
                {
                    case "EHLO": await writer.WriteLineAsync("250-fake\r\n250 8BITMIME"); break;
                    case "DATA":
                        await writer.WriteLineAsync("354 go");
                        var sb = new StringBuilder();
                        while (await reader.ReadLineAsync() is { } d && d != ".") sb.AppendLine(d);
                        await writer.WriteLineAsync("250 queued");
                        Received.TrySetResult(sb.ToString());
                        break;
                    case "QUIT": await writer.WriteLineAsync("221 bye"); _listener.Stop(); return;
                    default: await writer.WriteLineAsync("250 ok"); break;
                }
            }
        }
    }
}
