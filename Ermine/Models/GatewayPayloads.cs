using System;
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
    [property: JsonPropertyName("emojis")] List<Emoji>? Emojis,
    [property: JsonPropertyName("channel_unreads")] List<UnreadState>? ChannelUnreads
);

public record MessageDeleteEvent(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("channel")] string Channel
);

public record MessageUpdateData(
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("edited")] DateTime? Edited
);

public record MessageUpdateEvent(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("data")] MessageUpdateData Data
);