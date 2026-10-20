using FikaServer.Models;
using FikaServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace FikaServer.API;

/// <summary>Administrative prototype controls; not the production raid creation API.</summary>
[ApiController]
[Route("fika/api/dedicated-prototype")]
[RequireApiKey]
public sealed class DedicatedPrototypeController(
    DedicatedRaidInstanceService instances,
    DedicatedRaidWorkerSupervisor supervisor) : ControllerBase
{
    public sealed record StartRequest(string MapId, string OwnerId);

    [HttpGet("instances")]
    public IActionResult Instances()
    {
        supervisor.ReapExitedWorkers();
        return Ok(new { instances = instances.GetInstances(), heartbeats = supervisor.GetLastHeartbeats() });
    }

    [HttpPost("start")]
    public IActionResult Start([FromBody] StartRequest request)
    {
        supervisor.ReapExitedWorkers();
        if (string.IsNullOrWhiteSpace(request.MapId) || string.IsNullOrWhiteSpace(request.OwnerId))
            return BadRequest(new { error = "MapId and OwnerId are required." });

        var raidId = Guid.NewGuid().ToString("N");
        var reserved = instances.Reserve(raidId, request.MapId, request.OwnerId);
        if (reserved.Instance is null || reserved.Status != DedicatedRaidAdmissionService.AdmissionStatus.Reserved)
            return Conflict(new { status = reserved.Status.ToString(), soloAvailable = true });

        var exe = Path.Combine(AppContext.BaseDirectory, "FikaRaidWorker",
            OperatingSystem.IsWindows() ? "FikaRaidWorker.exe" : "FikaRaidWorker");
        var backend = $"{Request.Scheme}://127.0.0.1:{Request.Host.Port ?? (Request.IsHttps ? 443 : 80)}";
        var result = supervisor.Start(raidId, request.MapId, exe, backend);
        if (!result.Success)
        {
            instances.CompleteShutdown(raidId, failed: true);
            return StatusCode(503, new { error = result.Error });
        }

        return Accepted(new { raidId, processId = result.ProcessId, status = "Starting", playable = false });
    }

    [HttpPost("{raidId}/stop")]
    public async Task<IActionResult> Stop(string raidId)
    {
        return await supervisor.StopAsync(raidId, TimeSpan.FromSeconds(5))
            ? Ok(new { stopped = true }) : NotFound();
    }
}
