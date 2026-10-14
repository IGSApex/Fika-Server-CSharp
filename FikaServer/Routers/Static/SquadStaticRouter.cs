using FikaServer.Models.Fika.WebSocket.Notifications;
using FikaServer.WebSockets;
using FikaServer.Models.Fika.Routes.Squad;
using FikaServer.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Models.Common;

namespace FikaServer.Routers.Static;

/// <summary>
/// Squad endpoints use the authenticated session ID, never a caller-supplied sender.
/// </summary>
[Injectable]
public sealed class SquadStaticRouter(SquadService squads, NotificationWebSocket notifications, HttpResponseUtil response, JsonUtil jsonUtil) : StaticRouter(jsonUtil, [
    new RouteAction<SquadRequest>("/fika/squad/create", (url, data, session, output, cancellationToken) =>
    {
        var squadId = squads.CreateSquad(session.ToString());
        return new ValueTask<string>(response.NoBody(new { success = !string.IsNullOrEmpty(squadId), squadId }));
    }),
    new RouteAction<SquadRequest>("/fika/squad/invite", async (url, data, session, output, cancellationToken) =>
    {
        var invite = squads.InvitePlayer(session.ToString(), data.RecipientId);
        if (invite != null)
            await notifications.SendToSessionAsync(data.RecipientId, new SquadNotification { InviteId = invite.Id, SquadId = invite.SquadId, SenderId = invite.Sender });
        return response.NoBody(new { success = invite != null, inviteId = invite?.Id });
    }),
    new RouteAction<SquadRequest>("/fika/squad/respond", async (url, data, session, output, cancellationToken) =>
    {
        var success = squads.Respond(session.ToString(), data.InviteId, data.Accept);
        if (success && data.Accept)
        {
            var members = squads.GetMembers(session.ToString());
            foreach (var member in members)
                await notifications.SendToSessionAsync(member, new SquadNotification { Type = FikaServer.Models.Enums.EFikaNotification.SquadUpdated, SquadId = squads.GetSquadId(member) ?? string.Empty, Members = members });
        }
        return response.NoBody(new { success });
    }),
    new RouteAction<SquadRequest>("/fika/squad/kick", async (url, data, session, output, cancellationToken) =>
    {
        var previousMembers = squads.GetMembers(session.ToString());
        var success = squads.Kick(session.ToString(), data.RecipientId);
        if (success)
            foreach (var member in previousMembers)
                await notifications.SendToSessionAsync(member, new SquadNotification
                {
                    Type = FikaServer.Models.Enums.EFikaNotification.SquadUpdated,
                    SquadId = squads.GetSquadId(member) ?? string.Empty,
                    Members = squads.GetMembers(member)
                });
        return response.NoBody(new { success });
    }),
    new RouteAction<SquadRequest>("/fika/squad/leave", async (url, data, session, output, cancellationToken) =>
    {
        var previousMembers = squads.GetMembers(session.ToString());
        var success = squads.Leave(session.ToString());
        if (success)
        {
            foreach (var member in previousMembers)
                await notifications.SendToSessionAsync(member, new SquadNotification {
                    Type = FikaServer.Models.Enums.EFikaNotification.SquadUpdated,
                    SquadId = squads.GetSquadId(member) ?? string.Empty,
                    Members = squads.GetMembers(member)
                });
        }
        return response.NoBody(new { success });
    }),
    new RouteAction<SquadRequest>("/fika/squad/status", (url, data, session, output, cancellationToken) =>
        new ValueTask<string>(response.NoBody(new {
            squadId = squads.GetSquadId(session.ToString()),
            members = squads.GetMembers(session.ToString()),
    isLeader = squads.IsLeader(session.ToString()),
            pendingInvites = squads.GetPendingInvites(session.ToString())
        })))
])
{
}
