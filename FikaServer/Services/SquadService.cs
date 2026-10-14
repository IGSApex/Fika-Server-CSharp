using System;
using System.Collections.Generic;
using System.Linq;
using SPTarkov.DI.Annotations;

namespace FikaServer.Services;

/// <summary>
/// Server-authoritative, transient pre-raid squads. Profiles are never allowed to
/// choose their own squad IDs; membership changes only through accepted invitations.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class SquadService
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Squad> _squads = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _memberships = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Invite> _invites = new(StringComparer.Ordinal);

    public sealed record Squad(string Id, string Leader, HashSet<string> Members);
    public sealed record Invite(string Id, string SquadId, string Sender, string Recipient, DateTime ExpiresAtUtc);

    public string CreateSquad(string leader)
    {
        if (string.IsNullOrWhiteSpace(leader))
            return string.Empty;
        lock (_sync)
        {
            if (_memberships.TryGetValue(leader, out var existing))
                return existing;
            var id = Guid.NewGuid().ToString("N");
            _squads[id] = new Squad(id, leader, new HashSet<string>(StringComparer.Ordinal) { leader });
            _memberships[leader] = id;
            return id;
        }
    }

    public bool IsLeader(string player)
    {
        lock (_sync)
            return _memberships.TryGetValue(player, out var id)
                && _squads.TryGetValue(id, out var squad) && squad.Leader == player;
    }

    public Invite? InvitePlayer(string sender, string recipient)
    {
        if (string.IsNullOrWhiteSpace(sender) || string.IsNullOrWhiteSpace(recipient) || sender == recipient)
            return null;
        lock (_sync)
        {
            if (_memberships.ContainsKey(recipient))
                return null;
            foreach (var expired in _invites.Values.Where(x => x.ExpiresAtUtc <= DateTime.UtcNow).Select(x => x.Id).ToArray())
                _invites.Remove(expired);
            if (!_memberships.TryGetValue(sender, out var squadId))
            {
                squadId = Guid.NewGuid().ToString("N");
                _squads[squadId] = new Squad(squadId, sender, new HashSet<string>(StringComparer.Ordinal) { sender });
                _memberships[sender] = squadId;
            }
            if (_squads[squadId].Leader != sender || _squads[squadId].Members.Count >= 5)
                return null;
            var invite = new Invite(Guid.NewGuid().ToString("N"), squadId, sender, recipient, DateTime.UtcNow.AddMinutes(2));
            foreach (var prior in _invites.Values.Where(x => x.Sender == sender && x.Recipient == recipient).Select(x => x.Id).ToArray())
                _invites.Remove(prior);
            _invites[invite.Id] = invite;
            return invite;
        }
    }

    public bool Respond(string recipient, string inviteId, bool accept)
    {
        lock (_sync)
        {
            if (!_invites.TryGetValue(inviteId, out var invite) || invite.Recipient != recipient || invite.ExpiresAtUtc <= DateTime.UtcNow)
                return false;
            _invites.Remove(inviteId);
            if (!accept)
                return true;
            if (_memberships.ContainsKey(recipient) || !_squads.TryGetValue(invite.SquadId, out var squad)
                || squad.Leader != invite.Sender || squad.Members.Count >= 5)
                return false;
            squad.Members.Add(recipient);
            _memberships[recipient] = squad.Id;
            foreach (var other in _invites.Values.Where(x => x.Recipient == recipient).Select(x => x.Id).ToArray())
                _invites.Remove(other);
            return true;
        }
    }

    public bool Leave(string player)
    {
        lock (_sync)
        {
            if (!_memberships.Remove(player, out var squadId) || !_squads.TryGetValue(squadId, out var squad))
                return false;
            squad.Members.Remove(player);
            if (squad.Leader == player || squad.Members.Count == 0)
            {
                foreach (var member in squad.Members)
                    _memberships.Remove(member);
                _squads.Remove(squadId);
                foreach (var inviteId in _invites.Values.Where(x => x.SquadId == squadId).Select(x => x.Id).ToArray())
                    _invites.Remove(inviteId);
            }
            return true;
        }
    }

    public string? GetSquadId(string player)
    {
        lock (_sync)
            return _memberships.GetValueOrDefault(player);
    }

    public string[] GetMembers(string player)
    {
        lock (_sync)
            return _memberships.TryGetValue(player, out var id) && _squads.TryGetValue(id, out var squad)
                ? squad.Members.ToArray() : [];
    }

    public Invite[] GetPendingInvites(string player)
    {
        lock (_sync)
            return _invites.Values.Where(x => x.Recipient == player && x.ExpiresAtUtc > DateTime.UtcNow).ToArray();
    }
}
