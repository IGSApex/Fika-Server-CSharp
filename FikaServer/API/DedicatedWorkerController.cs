using FikaServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace FikaServer.API;

/// <summary>Loopback-only lifecycle endpoint for server-launched prototype workers.</summary>
[ApiController]
[Route("fika/api/dedicated-worker")]
public sealed class DedicatedWorkerController(DedicatedRaidWorkerSupervisor supervisor) : ControllerBase
{
    public sealed record WorkerReport(string RaidId, string MapId, string State);

    [HttpPost("report")]
    public IActionResult Report([FromBody] WorkerReport report)
    {
        if (HttpContext.Connection.RemoteIpAddress is not { } remote ||
            !System.Net.IPAddress.IsLoopback(remote))
            return StatusCode(403);

        if (!Request.Headers.TryGetValue("X-Fika-Worker-Token", out var token) ||
            string.IsNullOrWhiteSpace(report.RaidId) ||
            string.IsNullOrWhiteSpace(report.MapId) ||
            string.IsNullOrWhiteSpace(report.State))
            return Unauthorized();

        return supervisor.Report(report.RaidId, report.MapId, token.ToString(), report.State)
            ? Ok() : Unauthorized();
    }
}
