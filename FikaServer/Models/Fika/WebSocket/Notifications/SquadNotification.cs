using System.Text.Json.Serialization;
using FikaServer.Models.Enums;

namespace FikaServer.Models.Fika.WebSocket.Notifications;

public record SquadNotification : IFikaNotification
{
    [JsonPropertyName("type")]
    public EFikaNotification Type { get; set; } = EFikaNotification.SquadInvite;

    [JsonPropertyName("squadId")]
    public string SquadId { get; set; } = string.Empty;

    [JsonPropertyName("inviteId")]
    public string InviteId { get; set; } = string.Empty;

    [JsonPropertyName("senderId")]
    public string SenderId { get; set; } = string.Empty;

    [JsonPropertyName("members")]
    public string[] Members { get; set; } = [];
}
