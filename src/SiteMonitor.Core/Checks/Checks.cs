using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using SiteMonitor.Core.Models;

namespace SiteMonitor.Core.Checks;

public interface ICheck
{
    string Kind { get; }
    Task<CheckResult> RunAsync(string host, int timeoutMs, CancellationToken ct);
}

public sealed class PingCheck : ICheck
{
    public string Kind => "Ping";

    public async Task<CheckResult> RunAsync(string host, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, TimeSpan.FromMilliseconds(timeoutMs), cancellationToken: ct).ConfigureAwait(false);
            return reply.Status == IPStatus.Success
                ? CheckResult.Ok($"Ping reply from {reply.Address}", reply.RoundtripTime)
                : CheckResult.Fail($"Ping {reply.Status}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return CheckResult.Fail("Ping timed out");
        }
        catch (PingException ex)
        {
            return CheckResult.Fail("Ping failed: " + (ex.InnerException?.Message ?? ex.Message));
        }
    }
}

public sealed class TcpCheck : ICheck
{
    private readonly int _port;
    public TcpCheck(int port) => _port = port;
    public string Kind => $"TCP:{_port}";

    public async Task<CheckResult> RunAsync(string host, int timeoutMs, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, _port, cts.Token).ConfigureAwait(false);
            return CheckResult.Ok($"TCP {_port} open", sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return CheckResult.Fail($"TCP {_port} connect timed out after {timeoutMs} ms", sw.ElapsedMilliseconds);
        }
        catch (SocketException ex)
        {
            return CheckResult.Fail($"TCP {_port}: {ex.SocketErrorCode}", sw.ElapsedMilliseconds);
        }
    }
}

public sealed class HttpCheck : ICheck
{
    // One shared client for the whole process: connection pooling, low allocation.
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
        AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
    })
    {
        Timeout = Timeout.InfiniteTimeSpan, // per-request timeout via CancellationToken
        DefaultRequestHeaders = { { "User-Agent", "SiteMonitor/1.0 (uptime check)" } }
    };

    private readonly int? _expectedStatus;
    private readonly string? _keyword;

    public HttpCheck(int? expectedStatus, string? keyword)
    {
        _expectedStatus = expectedStatus;
        _keyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword;
    }

    public string Kind => "HTTP";

    public async Task<CheckResult> RunAsync(string url, int timeoutMs, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        try
        {
            // Keyword needs a body → GET. Otherwise HEAD is cheapest; fall back to GET if HEAD is rejected.
            HttpResponseMessage resp;
            if (_keyword is null)
            {
                resp = await Client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url), HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                if (resp.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                {
                    resp.Dispose();
                    resp = await Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                }
            }
            else
            {
                resp = await Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            }

            using (resp)
            {
                var code = (int)resp.StatusCode;
                bool statusOk = _expectedStatus is { } exp ? code == exp : code is >= 200 and < 400;
                if (!statusOk)
                    return CheckResult.Fail($"HTTP {code} {resp.ReasonPhrase}" + (_expectedStatus is { } e ? $" (expected {e})" : ""), sw.ElapsedMilliseconds);

                if (_keyword is not null)
                {
                    var body = await ReadLimitedAsync(resp, 2 * 1024 * 1024, cts.Token).ConfigureAwait(false);
                    if (!body.Contains(_keyword, StringComparison.OrdinalIgnoreCase))
                        return CheckResult.Fail($"HTTP {code} but keyword \"{_keyword}\" not found", sw.ElapsedMilliseconds);
                }
                return CheckResult.Ok($"HTTP {code}", sw.ElapsedMilliseconds);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return CheckResult.Fail($"HTTP timed out after {timeoutMs} ms", sw.ElapsedMilliseconds);
        }
        catch (HttpRequestException ex)
        {
            return CheckResult.Fail("HTTP error: " + (ex.InnerException?.Message ?? ex.Message), sw.ElapsedMilliseconds);
        }
    }

    private static async Task<string> ReadLimitedAsync(HttpResponseMessage resp, int maxBytes, CancellationToken ct)
    {
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buffer = new byte[maxBytes];
        int total = 0, read;
        while (total < maxBytes && (read = await stream.ReadAsync(buffer.AsMemory(total, maxBytes - total), ct).ConfigureAwait(false)) > 0)
            total += read;
        return System.Text.Encoding.UTF8.GetString(buffer, 0, total);
    }
}

/// <summary>Tries each check in order; succeeds on the first success. Used by Auto mode for bare hosts.</summary>
public sealed class FallbackCheck : ICheck
{
    private readonly ICheck[] _checks;
    public FallbackCheck(params ICheck[] checks) => _checks = checks;
    public string Kind => string.Join("/", _checks.Select(c => c.Kind));

    public async Task<CheckResult> RunAsync(string host, int timeoutMs, CancellationToken ct)
    {
        var errors = new List<string>();
        foreach (var check in _checks)
        {
            var r = await check.RunAsync(host, timeoutMs, ct).ConfigureAwait(false);
            if (r.Success) return r;
            errors.Add(r.Message);
        }
        return CheckResult.Fail(string.Join("; ", errors));
    }
}
