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
public sealed class SquadStaticRouter(SquadService squads, HttpResponseUtil response, JsonUtil jsonUtil) : StaticRouter(jsonUtil, [
    new RouteAction<SquadRequest>("/fika/squad/invite", (url, data, session, output, cancellationToken) =>
        new ValueTask<string>(response.NoBody(squads.InvitePlayer(session.ToString(), data.RecipientId)))),
    new RouteAction<SquadRequest>("/fika/squad/respond", (url, data, session, output, cancellationToken) =>
        new ValueTask<string>(response.NoBody(new { success = squads.Respond(session.ToString(), data.InviteId, data.Accept) }))),
    new RouteAction<SquadRequest>("/fika/squad/leave", (url, data, session, output, cancellationToken) =>
        new ValueTask<string>(response.NoBody(new { success = squads.Leave(session.ToString()) }))),
    new RouteAction<SquadRequest>("/fika/squad/status", (url, data, session, output, cancellationToken) =>
        new ValueTask<string>(response.NoBody(new {
            squadId = squads.GetSquadId(session.ToString()),
            members = squads.GetMembers(session.ToString()),
            pendingInvites = squads.GetPendingInvites(session.ToString())
        })))
])
{
}
