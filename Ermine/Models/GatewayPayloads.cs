using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Ermine.Models;

public record BaseGatewayEvent([property: JsonPropertyName("type")] string Type);

public record AuthenticatePayload(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("token")] string Token);

public record ReadyEvent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("servers")]
    List<Server>? Servers,
    [property: JsonPropertyName("users")] List<User>? Users,
    [property: JsonPropertyName("channels")] List<Channel>? Channels,
    [property: JsonPropertyName("emojis")] List<Emoji>? Emojis
);