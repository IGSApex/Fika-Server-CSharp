using FikaServer.Models.Enums;
using FikaServer.Models.Fika.Config;
using FikaServer.Services.Headless;
using Microsoft.AspNetCore.Mvc;

namespace FikaServer.API;

/// <summary>Matchmaking capacity for playable Fika headless hosts.</summary>
[ApiController]
[Route("fika/api/dedicated-capacity")]
public sealed class DedicatedCapacityController(
    HeadlessService headless,
    FikaServerConfig config) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var clients = headless.HeadlessClients.Values.ToArray();
        var active = clients.Count(x => x.State == EHeadlessStatus.IN_RAID);
        var ready = clients.Count(x => x.State == EHeadlessStatus.READY);
        var maximum = Math.Max(0, config.DedicatedServers.ConcurrentServer);
        return Ok(new
        {
            active,
            maximum,
            ready,
            canStart = active < maximum && ready > 0,
            maxPlayersPerRaid = Math.Max(0, config.DedicatedServers.MaxPlayersPerRaid),
            maxHotJoinTime = Math.Max(0, config.DedicatedServers.MaxHotJoinTime)
        });
    }
}
