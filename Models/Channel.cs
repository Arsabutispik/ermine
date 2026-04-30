using System;
using System.Text.Json.Serialization;

namespace Ermine.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChannelType
{
    SavedMessages,
    DirectMessage,
    Group,
    TextChannel
}

[JsonPolymorphic(
    TypeDiscriminatorPropertyName = "channel_type",
    IgnoreUnrecognizedTypeDiscriminators = true,
    UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToBaseType)]
[JsonDerivedType(typeof(SavedMessagesChannel), typeDiscriminator: "SavedMessages")]
[JsonDerivedType(typeof(DirectMessageChannel), typeDiscriminator: "DirectMessage")]
[JsonDerivedType(typeof(Group), typeDiscriminator: "Group")]
[JsonDerivedType(typeof(TextChannel), typeDiscriminator: "TextChannel")]
public record Channel
{
    [JsonPropertyName("_id")]
    public string Id { get; init; } = string.Empty;
    
    [JsonIgnore]
    public ChannelType ChannelType => this switch
    {
        SavedMessagesChannel => ChannelType.SavedMessages,
        DirectMessageChannel => ChannelType.DirectMessage,
        Group => ChannelType.Group,
        TextChannel => ChannelType.TextChannel,
        _ => throw new NotImplementedException("Unknown channel type")
    };
    
    [JsonIgnore]
    public string DisplayName => this switch
    {
        TextChannel t => t.Name,
        Group g => g.Name,
        SavedMessagesChannel => "Saved Notes",
        _ => "Unknown Channel"
    };
    
    [JsonIgnore]
    public string? IconUrl => (this as TextChannel)?.IconUrl;
}

public record SavedMessagesChannel : Channel
{
    [JsonPropertyName("user")]
    public string User { get; init; } = string.Empty;
}

public record DirectMessageChannel : Channel
{
    [JsonPropertyName("active")]
    public bool Active { get; init; }
    
    [JsonPropertyName("recipients")]
    public System.Collections.Generic.IReadOnlyList<string> Recipients { get; init; } = [];
    
    [JsonPropertyName("last_message_id")]
    public string? LastMessageId { get; init; }
}

public record Group : Channel
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
    
    [JsonPropertyName("owner")]
    public string Owner { get; init; } = string.Empty;
    
    [JsonPropertyName("recipients")]
    public System.Collections.Generic.IReadOnlyList<string> Recipients { get; init; } = [];
    
    [JsonPropertyName("description")]
    public string? Description { get; init; }
    
    [JsonPropertyName("icon")]
    public Attachment? Icon { get; init; }
    
    [JsonPropertyName("last_message_id")]
    public string? LastMessageId { get; init; }
    
    [JsonPropertyName("nsfw")]
    public bool? Nsfw { get; init; }
    
    [JsonPropertyName("permissions")]
    public long? Permissions { get; init; }
}

public record OverrideField
{
    [JsonPropertyName("a")]
    public long A { get; init; }
    
    [JsonPropertyName("d")]
    public long D { get; init; }
}

public record VoiceInformation
{
    [JsonPropertyName("max_users")]
    public uint? MaxUsers { get; init; }
}

public record TextChannel : Channel
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
    
    [JsonPropertyName("server")]
    public string Server { get; init; } = string.Empty;
    
    [JsonPropertyName("default_permissions")]
    public OverrideField? DefaultPermissions { get; init; }
    
    [JsonPropertyName("description")]
    public string? Description { get; init; }
    
    [JsonPropertyName("icon")]
    public Attachment? Icon { get; init; }
    
    [JsonPropertyName("last_message_id")]
    public string? LastMessageId { get; init; }
    
    [JsonPropertyName("nsfw")]
    public bool? Nsfw { get; init; }
    
    [JsonPropertyName("role_permissions")]
    public System.Collections.Generic.Dictionary<string, OverrideField>? RolePermissions { get; init; }
    
    [JsonPropertyName("slowmode")]
    public ulong? Slowmode { get; init; }
    
    [JsonPropertyName("voice")]
    public VoiceInformation? Voice { get; init; }
    
    public new string? IconUrl => Icon != null 
        ? $"{ApiClient.AutumnUrl}/icons/{Icon.Id}" 
        : null;
}
