using System.Text.Json.Serialization;

namespace Ermine.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EmojiParentType
{
    Server,
    Detached
}

public class EmojiParent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public EmojiParentType Type { get; set; }
}

public record Emoji(
    [property: JsonPropertyName("_id")] string Id,
    [property: JsonPropertyName("creator_id")]
    string CreatorId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("parent")] EmojiParent Parent,
    [property: JsonPropertyName("animated")]
    bool Animated = false,
    [property: JsonPropertyName("nsfw")] bool Nsfw = false
 )
{
    [JsonIgnore] public bool IsServerEmoji => Parent?.Type == EmojiParentType.Server;
}