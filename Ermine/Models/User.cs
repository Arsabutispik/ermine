using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Ermine.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Relationship
{
    None,
    User,
    Friend,
    Outgoing,
    Incoming,
    Blocked,
    BlockedOther,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UserPresence
{
    Online,
    Idle,
    Focus,
    Busy,
    Invisible
}

public record StoatRelations(
    [property: JsonPropertyName("_id")] string Id,
    [property: JsonPropertyName("status")] Relationship Status
);

public record UserStatus(
    [property: JsonPropertyName("presence")] UserPresence? Presence,
    [property: JsonPropertyName("text")] string Text
    );

public record BotInformation(
    [property: JsonPropertyName("owner")] string Owner
);

public record User(
    [property: JsonPropertyName("_id")] string Id,
    [property: JsonPropertyName("discriminator")] string Discriminator,
    [property: JsonPropertyName("online")] bool Online,
    [property: JsonPropertyName("relationship")] Relationship Relationship,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("avatar")] Attachment? Avatar,
    [property: JsonPropertyName("badges")] uint Badges,
    [property: JsonPropertyName("bot")]  BotInformation? Bot,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("flags")] uint Flags,
    [property: JsonPropertyName("privileged")]  bool Privileged,
    [property: JsonPropertyName("relations")] IReadOnlyList<StoatRelations>? Relations = null,
    [property: JsonPropertyName("status")] UserStatus? Status = null
)
{
    [JsonIgnore]
    public string? AvatarUrl => Avatar?.Url;

    /// <summary>
    /// Fetches the currently authenticated user.
    /// </summary>
    public static async Task<User?> GetCurrentUserAsync()
    {
        // Calls the generic helper from the centralized ApiClient
        return await ApiClient.GetAsync<User>("users/@me");
    }

    /// <summary>
    /// Example of how you could fetch a specific user in the future.
    /// </summary>
    public static async Task<User?> GetUserAsync(string userId)
    {
        return await ApiClient.GetAsync<User>($"users/{userId}");
    }
}