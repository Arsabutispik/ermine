using System.Text.Json.Serialization;

namespace Ermine.Models;

public record UnreadStateId
{
    [JsonPropertyName("channel")]
    public string Channel { get; init; } = string.Empty;

    [JsonPropertyName("user")]
    public string User { get; init; } = string.Empty;
}

public record UnreadState
{
    [JsonPropertyName("_id")]
    public UnreadStateId Id { get; init; } = new();

    [JsonPropertyName("last_id")]
    public string? LastId { get; init; }

    [JsonPropertyName("mentions")]
    public string[]? Mentions { get; init; }
}