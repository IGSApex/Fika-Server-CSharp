using System.Net.Http.Json;

static string? Arg(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

var raidId = Arg(args, "--raid-id");
var mapId = Arg(args, "--map-id");
var backend = Arg(args, "--backend-url");
var token = Arg(args, "--worker-token");
if (string.IsNullOrWhiteSpace(raidId) || string.IsNullOrWhiteSpace(mapId) ||
    string.IsNullOrWhiteSpace(backend) || string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("Missing --raid-id, --map-id, --backend-url or --worker-token.");
    return 2;
}

using var client = new HttpClient { BaseAddress = new Uri(backend.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(8) };
client.DefaultRequestHeaders.Add("X-Fika-Worker-Token", token);
using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stopping.Cancel(); };

async Task<bool> Report(string state)
{
    try
    {
        using var response = await client.PostAsJsonAsync("fika/api/dedicated-worker/report",
            new { raidId, mapId, state }, stopping.Token);
        if (!response.IsSuccessStatusCode)
            Console.Error.WriteLine($"Worker report {state} rejected: {(int)response.StatusCode}");
        return response.IsSuccessStatusCode;
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        Console.Error.WriteLine($"Worker report {state} failed: {ex.Message}");
        return false;
    }
}

Console.WriteLine($"Fika raid worker prototype: {raidId} / {mapId}");
if (!await Report("ready")) return 1;
if (!await Report("running")) return 1;

try
{
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
    while (await timer.WaitForNextTickAsync(stopping.Token))
    {
        if (!await Report("heartbeat")) return 1;
    }
}
catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }

// This prototype sends lifecycle signals only. It does not simulate an EFT raid.
if (!stopping.IsCancellationRequested) return 1;
try
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await client.PostAsJsonAsync("fika/api/dedicated-worker/report",
        new { raidId, mapId, state = "stopped" }, timeout.Token);
}
catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
{
    Console.Error.WriteLine($"Shutdown report failed: {ex.Message}");
}
return 0;
