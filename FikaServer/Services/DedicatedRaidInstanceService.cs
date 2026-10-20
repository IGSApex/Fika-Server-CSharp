using FikaServer.Models.Fika.Config;
using SPTarkov.DI.Annotations;

namespace FikaServer.Services;

/// <summary>
/// Manages the lifecycle of prospective dedicated raid instances.
/// This is orchestration only: an EFT simulation worker must be attached before
/// a reservation can become a playable raid.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class DedicatedRaidInstanceService(
    DedicatedRaidAdmissionService admission,
    FikaServerConfig config)
{
    public enum InstanceState { Starting, Ready, Running, Draining, Stopped, Failed }

    public sealed record InstanceSnapshot(
        string RaidId, string MapId, InstanceState State, int ReservedPlayers,
        DateTimeOffset CreatedAt, DateTimeOffset? StartedAt);

    public sealed record StartResult(
        DedicatedRaidAdmissionService.AdmissionStatus Status,
        InstanceSnapshot? Instance = null);

    private sealed class Instance(string raidId, string mapId, string ownerId)
    {
        public string RaidId { get; } = raidId;
        public string MapId { get; } = mapId;
        public string OwnerId { get; } = ownerId;
        public InstanceState State { get; set; } = InstanceState.Starting;
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? StartedAt { get; set; }
        public HashSet<string> Players { get; } = new(StringComparer.Ordinal);
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Instance> _instances = new(StringComparer.Ordinal);

    private static InstanceSnapshot Snapshot(Instance instance) =>
        new(instance.RaidId, instance.MapId, instance.State, instance.Players.Count,
            instance.CreatedAt, instance.StartedAt);

    public IReadOnlyList<InstanceSnapshot> GetInstances()
    {
        lock (_gate)
            return _instances.Values.Select(Snapshot).ToArray();
    }

    /// <summary>
    /// Atomically allocates a slot. Does not start a worker or publish a joinable raid.
    /// </summary>
    public StartResult Reserve(string raidId, string mapId, string ownerId)
    {
        lock (_gate)
        {
            var result = admission.ReserveNewRaid(raidId, mapId, ownerId);
            if (!result.Accepted)
                return new(result.Status);

            if (result.Status == DedicatedRaidAdmissionService.AdmissionStatus.AlreadyReserved)
            {
                return _instances.TryGetValue(result.RaidId!, out var existing)
                    ? new(result.Status, Snapshot(existing))
                    : new(DedicatedRaidAdmissionService.AdmissionStatus.InvalidRequest);
            }

            var instance = new Instance(raidId, mapId, ownerId);
            instance.Players.Add(ownerId);
            _instances.Add(raidId, instance);
            return new(result.Status, Snapshot(instance));
        }
    }

    /// <summary>Only the future worker supervisor should call this after worker initialization.</summary>
    public bool MarkReady(string raidId)
    {
        lock (_gate)
        {
            if (!_instances.TryGetValue(raidId, out var instance) || instance.State != InstanceState.Starting)
                return false;
            instance.State = InstanceState.Ready;
            return true;
        }
    }

    /// <summary>Starts the hot-join clock when simulation begins.</summary>
    public bool MarkRunning(string raidId, DateTimeOffset startedAt)
    {
        lock (_gate)
        {
            if (!_instances.TryGetValue(raidId, out var instance) || instance.State != InstanceState.Ready)
                return false;
            if (!admission.MarkRunning(raidId, startedAt))
                return false;
            instance.StartedAt = startedAt;
            instance.State = InstanceState.Running;
            return true;
        }
    }

    public DedicatedRaidAdmissionService.AdmissionResult ReserveJoin(
        string raidId, string mapId, string playerId, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_instances.TryGetValue(raidId, out var instance) ||
                instance.State is not (InstanceState.Ready or InstanceState.Running))
                return new(DedicatedRaidAdmissionService.AdmissionStatus.RaidNotFound);
            var result = admission.ReserveJoin(raidId, mapId, playerId, now);
            if (result.Accepted && result.RaidId == raidId)
                instance.Players.Add(playerId);
            return result;
        }
    }

    public void ReleasePlayer(string playerId)
    {
        lock (_gate)
        {
            admission.ReleasePlayer(playerId);
            foreach (var instance in _instances.Values)
                instance.Players.Remove(playerId);
        }
    }

    public bool BeginShutdown(string raidId)
    {
        lock (_gate)
        {
            if (!_instances.TryGetValue(raidId, out var instance) ||
                instance.State is InstanceState.Draining or InstanceState.Stopped or InstanceState.Failed)
                return false;
            instance.State = InstanceState.Draining;
            return true;
        }
    }

    /// <summary>Release slots on clean shutdown or failed worker startup.</summary>
    public bool CompleteShutdown(string raidId, bool failed = false)
    {
        lock (_gate)
        {
            if (!_instances.Remove(raidId, out var instance))
                return false;
            instance.State = failed ? InstanceState.Failed : InstanceState.Stopped;
            admission.ReleaseRaid(raidId);
            return true;
        }
    }
}
