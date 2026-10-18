using System.Text.Json.Serialization;

namespace FikaServer.Models.Fika.Config;

/// <summary>
/// Configuration for the planned server-owned raid instance runtime.
/// These options do not start dedicated simulation workers until the runtime is implemented.
/// </summary>
public sealed record FikaConfigDedicatedServers
{
    [JsonPropertyName("concurrentServer")]
    public int ConcurrentServer { get; set; } = 1;

    [JsonPropertyName("restartAfterRaid")]
    public bool RestartAfterRaid { get; set; } = true;

    [JsonPropertyName("maxPlayersPerRaid")]
    public int MaxPlayersPerRaid { get; set; } = 5;

    [JsonPropertyName("setLevelToAverageOfLobby")]
    public bool SetLevelToAverageOfLobby { get; set; } = true;
}
