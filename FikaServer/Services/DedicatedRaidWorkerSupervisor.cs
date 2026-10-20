using System.Diagnostics;
using System.Security.Cryptography;
using SPTarkov.DI.Annotations;

namespace FikaServer.Services;

/// <summary>
/// Supervises external dedicated raid worker processes. A worker executable must
/// be supplied explicitly; this service does not provide EFT simulation.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class DedicatedRaidWorkerSupervisor(DedicatedRaidInstanceService instances)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Process> _workers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _heartbeats = new(StringComparer.Ordinal);

    public sealed record WorkerStartResult(bool Success, string? Error = null, int? ProcessId = null);

    /// <summary>
    /// Starts an explicitly configured worker executable with raid and map identifiers.
    /// Does not mark the instance ready: a future authenticated readiness handshake must do so.
    /// </summary>
    public WorkerStartResult Start(string raidId, string mapId, string executablePath, string backendUrl)
    {
        if (string.IsNullOrWhiteSpace(raidId) || string.IsNullOrWhiteSpace(mapId) ||
            string.IsNullOrWhiteSpace(executablePath) || string.IsNullOrWhiteSpace(backendUrl))
            return new(false, "Missing raid ID, map ID, or worker executable.");

        if (!File.Exists(executablePath))
            return new(false, "Dedicated raid worker executable was not found.");

        lock (_gate)
        {
            if (_workers.ContainsKey(raidId))
                return new(false, "Worker already exists for this raid.");

            var instance = instances.GetInstances().FirstOrDefault(x => x.RaidId == raidId);
            if (instance is null || instance.State != DedicatedRaidInstanceService.InstanceState.Starting ||
                !string.Equals(instance.MapId, mapId, StringComparison.Ordinal))
                return new(false, "Raid is not reserved or is not in Starting state.");

            var info = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath))!
            };
            info.ArgumentList.Add("--raid-id");
            info.ArgumentList.Add(raidId);
            info.ArgumentList.Add("--map-id");
            info.ArgumentList.Add(mapId);
            info.ArgumentList.Add("--backend-url");
            info.ArgumentList.Add(backendUrl);
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            info.ArgumentList.Add("--worker-token");
            info.ArgumentList.Add(token);

            Process? process = null;
            try
            {
                process = Process.Start(info);
                if (process is null)
                {
                    instances.CompleteShutdown(raidId, failed: true);
                    return new(false, "Failed to start worker process.");
                }

                _workers.Add(raidId, process);
                _tokens.Add(raidId, token);
                _heartbeats.Add(raidId, DateTimeOffset.UtcNow);
                return new(true, ProcessId: process.Id);
            }
            catch (Exception ex)
            {
                process?.Dispose();
                _tokens.Remove(raidId);
                _heartbeats.Remove(raidId);
                instances.CompleteShutdown(raidId, failed: true);
                return new(false, ex.Message);
            }
        }
    }

    /// <summary>Accept lifecycle messages only from the worker launched for this raid.</summary>
    public bool Report(string raidId, string mapId, string token, string state)
    {
        lock (_gate)
        {
            if (!_tokens.TryGetValue(raidId, out var expected) ||
                !CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(expected),
                    System.Text.Encoding.UTF8.GetBytes(token)))
                return false;

            var instance = instances.GetInstances().FirstOrDefault(x => x.RaidId == raidId);
            if (instance is null || !string.Equals(instance.MapId, mapId, StringComparison.Ordinal))
                return false;
            if (!_workers.TryGetValue(raidId, out var process) || process.HasExited)
                return false;

            var accepted = state switch
            {
                "ready" => instances.MarkReady(raidId),
                "running" => instances.MarkRunning(raidId, DateTimeOffset.UtcNow),
                "heartbeat" => instance.State == DedicatedRaidInstanceService.InstanceState.Running,
                "stopped" => instances.BeginShutdown(raidId),
                _ => false
            };
            if (accepted) _heartbeats[raidId] = DateTimeOffset.UtcNow;
            return accepted;
        }
    }

    public IReadOnlyDictionary<string, DateTimeOffset> GetLastHeartbeats()
    {
        lock (_gate) return new Dictionary<string, DateTimeOffset>(_heartbeats);
    }

    /// <summary>
    /// Stop workers that have stopped reporting. The caller should run this periodically;
    /// this method only identifies stale workers, avoiding process waits under the lock.
    /// </summary>
    public async Task<int> StopUnresponsiveWorkersAsync(TimeSpan heartbeatTimeout, TimeSpan shutdownGrace)
    {
        string[] stale;
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            stale = _heartbeats
                .Where(pair => now - pair.Value > heartbeatTimeout)
                .Select(pair => pair.Key)
                .ToArray();
        }

        var stopped = 0;
        foreach (var raidId in stale)
        {
            // Recheck after releasing the lock in case a heartbeat arrived meanwhile.
            lock (_gate)
            {
                if (!_heartbeats.TryGetValue(raidId, out var last) ||
                    DateTimeOffset.UtcNow - last <= heartbeatTimeout)
                    continue;
            }

            if (await StopAsync(raidId, shutdownGrace))
                stopped++;
        }

        return stopped;
    }

    /// <summary>Poll process exits and release reserved slots for unexpectedly exited workers.</summary>
    public int ReapExitedWorkers()
    {
        lock (_gate)
        {
            var exited = _workers.Where(pair =>
            {
                try { return pair.Value.HasExited; }
                catch (InvalidOperationException) { return true; }
            }).Select(pair => pair.Key).ToArray();

            foreach (var raidId in exited)
            {
                _workers.Remove(raidId, out var process);
                process?.Dispose();
                _tokens.Remove(raidId);
                _heartbeats.Remove(raidId);
                instances.CompleteShutdown(raidId, failed: true);
            }

            return exited.Length;
        }
    }

    /// <summary>Stop a managed worker and release its instance. No process is killed on timeout.</summary>
    public async Task<bool> StopAsync(string raidId, TimeSpan gracePeriod)
    {
        Process process;
        lock (_gate)
        {
            if (!_workers.TryGetValue(raidId, out var activeProcess))
                return false;
            process = activeProcess;
            instances.BeginShutdown(raidId);
        }

        try
        {
            if (!process.HasExited)
            {
                if (process.CloseMainWindow())
                {
                    using var timeout = new CancellationTokenSource(gracePeriod);
                    try { await process.WaitForExitAsync(timeout.Token); }
                    catch (OperationCanceledException) { }
                }

                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                if (_workers.TryGetValue(raidId, out var current) && ReferenceEquals(current, process))
                {
                    _workers.Remove(raidId);
                    process.Dispose();
                    _tokens.Remove(raidId);
                    _heartbeats.Remove(raidId);
                    instances.CompleteShutdown(raidId);
                }
            }
        }
        return true;
    }
}
