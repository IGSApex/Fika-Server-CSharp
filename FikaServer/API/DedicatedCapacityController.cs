using FikaServer.Models.Fika.Config;
using FikaServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace FikaServer.API;

/// <summary>Read-only matchmaking capacity information. Does not expose worker credentials.</summary>
[ApiController]
[Route("fika/api/dedicated-capacity")]
public sealed class DedicatedCapacityController(
    DedicatedRaidAdmissionService admission,
    DedicatedRaidWorkerSupervisor supervisor,
    FikaServerConfig config) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        supervisor.ReapExitedWorkers();
        return Ok(new
    {
        active = admission.ActiveRaids,
        maximum = Math.Max(0, config.DedicatedServers.ConcurrentServer),
        maxPlayersPerRaid = Math.Max(0, config.DedicatedServers.MaxPlayersPerRaid),
        maxHotJoinTime = Math.Max(0, config.DedicatedServers.MaxHotJoinTime)
    });
    }
}
