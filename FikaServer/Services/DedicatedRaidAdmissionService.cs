using System.Collections.Concurrent;
using FikaServer.Models.Fika.Config;
using SPTarkov.DI.Annotations;

namespace FikaServer.Services;

/// <summary>
/// Prototype admission coordinator for future server-owned raid workers.
/// Does not launch or emulate an EFT simulation process.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class DedicatedRaidAdmissionService(FikaServerConfig config)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RaidSlot> _raids = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _playerReservations = new(StringComparer.Ordinal);

    public enum AdmissionStatus { Reserved, AlreadyReserved, CapacityReached, HotJoinExpired, RaidNotFound, InvalidRequest }

    public sealed record AdmissionResult(AdmissionStatus Status, string? RaidId = null)
    {
        public bool Accepted => Status is AdmissionStatus.Reserved or AdmissionStatus.AlreadyReserved;
    }

    private sealed class RaidSlot(string mapId)
    {
        public string MapId { get; } = mapId;
        public DateTimeOffset? StartedAt { get; set; }
        public HashSet<string> Players { get; } = new(StringComparer.Ordinal);
    }

    public int ActiveRaids
    {
        get { lock (_gate) return _raids.Count; }
    }

    /// <summary>Reserve an instance slot before a worker is launched. No worker is launched here.</summary>
    public AdmissionResult ReserveNewRaid(string raidId, string mapId, string playerId)
    {
        if (string.IsNullOrWhiteSpace(raidId) || string.IsNullOrWhiteSpace(mapId) || string.IsNullOrWhiteSpace(playerId))
            return new(AdmissionStatus.InvalidRequest);
        lock (_gate)
        {
            if (_playerReservations.TryGetValue(playerId, out var existing))
                return new(AdmissionStatus.AlreadyReserved, existing);
            if (_raids.ContainsKey(raidId))
                return new(AdmissionStatus.InvalidRequest);
            if (_raids.Count >= Math.Max(0, config.DedicatedServers.ConcurrentServer))
                return new(AdmissionStatus.CapacityReached);
            var slot = new RaidSlot(mapId);
            slot.Players.Add(playerId);
            _raids.Add(raidId, slot);
            _playerReservations.Add(playerId, raidId);
            return new(AdmissionStatus.Reserved, raidId);
        }
    }

    /// <summary>Mark the instant simulation actually starts, not the time of match creation.</summary>
    public bool MarkRunning(string raidId, DateTimeOffset startedAt)
    {
        lock (_gate)
        {
            if (!_raids.TryGetValue(raidId, out var slot) || slot.StartedAt.HasValue)
                return false;
            slot.StartedAt = startedAt;
            return true;
        }
    }

    public AdmissionResult ReserveJoin(string raidId, string mapId, string playerId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(raidId) || string.IsNullOrWhiteSpace(mapId) || string.IsNullOrWhiteSpace(playerId))
            return new(AdmissionStatus.InvalidRequest);
        lock (_gate)
        {
            if (_playerReservations.TryGetValue(playerId, out var existing))
                return new(AdmissionStatus.AlreadyReserved, existing);
            if (!_raids.TryGetValue(raidId, out var slot) || !string.Equals(slot.MapId, mapId, StringComparison.OrdinalIgnoreCase))
                return new(AdmissionStatus.RaidNotFound);
            if (slot.StartedAt.HasValue && (now - slot.StartedAt.Value).TotalSeconds > Math.Max(0, config.DedicatedServers.MaxHotJoinTime))
                return new(AdmissionStatus.HotJoinExpired);
            if (slot.Players.Count >= Math.Max(0, config.DedicatedServers.MaxPlayersPerRaid))
                return new(AdmissionStatus.CapacityReached);
            slot.Players.Add(playerId);
            _playerReservations.Add(playerId, raidId);
            return new(AdmissionStatus.Reserved, raidId);
        }
    }

    public void ReleasePlayer(string playerId)
    {
        lock (_gate)
        {
            if (_playerReservations.Remove(playerId, out var raidId) && _raids.TryGetValue(raidId, out var slot))
                slot.Players.Remove(playerId);
        }
    }

    /// <summary>Call on worker shutdown, startup failure or raid cleanup.</summary>
    public void ReleaseRaid(string raidId)
    {
        lock (_gate)
        {
            if (!_raids.Remove(raidId, out var slot)) return;
            foreach (var player in slot.Players)
                _playerReservations.Remove(player);
        }
    }
}
