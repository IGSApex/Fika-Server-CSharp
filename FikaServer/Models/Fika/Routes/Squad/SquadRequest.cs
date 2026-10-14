using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Utils;

namespace FikaServer.Models.Fika.Routes.Squad;

public record SquadRequest : IRequestData
{
    [JsonPropertyName("recipientId")]
    public string RecipientId { get; set; } = string.Empty;
    [JsonPropertyName("inviteId")]
    public string InviteId { get; set; } = string.Empty;
    [JsonPropertyName("accept")]
    public bool Accept { get; set; }
}
