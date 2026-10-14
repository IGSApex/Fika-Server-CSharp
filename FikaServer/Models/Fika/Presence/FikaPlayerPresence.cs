using FikaServer.Models.Enums;
using System.Text.Json.Serialization;

namespace FikaServer.Models.Fika.Presence;

public sealed record FikaPlayerPresence
{
    [JsonPropertyName("profileId")]
    public string ProfileId { get; set; } = string.Empty;

    [JsonPropertyName("squadId")]
    public string SquadId { get; set; } = string.Empty;

    [JsonPropertyName("nickname")]
    public string Nickname { get; set; } = string.Empty;

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("activity")]
    public EFikaPlayerPresences Activity { get; set; }

    [JsonPropertyName("activityStartedTimestamp")]
    public long ActivityStartedTimestamp { get; set; }

    [JsonPropertyName("raidInformation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public FikaRaidPresence? RaidInformation { get; set; }
}
