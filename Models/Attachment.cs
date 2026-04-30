using System;
using System.Text.Json.Serialization;

namespace Ermine.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(GenericFileMetadata), typeDiscriminator: "File")]
[JsonDerivedType(typeof(TextFileMetadata), typeDiscriminator: "Text")]
[JsonDerivedType(typeof(ImageFileMetadata), typeDiscriminator: "Image")]
[JsonDerivedType(typeof(VideoFileMetadata), typeDiscriminator: "Video")]
[JsonDerivedType(typeof(AudioFileMetadata), typeDiscriminator: "Audio")]
public abstract record FileMetadata;

public record GenericFileMetadata : FileMetadata;

public record TextFileMetadata : FileMetadata;

public record ImageFileMetadata(
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("animated")] bool? Animated = null,
    [property: JsonPropertyName("thumbhash")] int[]? Thumbhash = null
) : FileMetadata;

public record VideoFileMetadata(
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height
) : FileMetadata;

public record AudioFileMetadata : FileMetadata;

public record Attachment(
    [property: JsonPropertyName("_id")] string Id,
    [property: JsonPropertyName("content_type")]
    string ContentType,
    [property: JsonPropertyName("filename")]
    string Filename,
    [property: JsonPropertyName("metadata")]
    FileMetadata? Metadata,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("tag")] string Tag,
    [property: JsonPropertyName("deleted")]
    bool? Deleted = null,
    [property: JsonPropertyName("message_id")]
    string? MessageId = null,
    [property: JsonPropertyName("object_id")]
    string? ObjectId = null,
    [property: JsonPropertyName("reported")]
    bool? Reported = null,
    [property: JsonPropertyName("server_id")]
    string? ServerId = null,
    [property: JsonPropertyName("user_id")]
    string? UserId = null
)
{
    [JsonIgnore]
    public string Url => $"{ApiClient.AutumnUrl}/{Tag}/{Id}/{Filename}";
    
    [JsonIgnore]
    public bool IsImage => ContentType?.StartsWith("image/") == true;
    
    [JsonIgnore]
    public double DisplayWidth
    {
        get
        {
            if (Metadata is not ImageFileMetadata img || img.Width == 0) return 400;
            var scale = Math.Min(400.0 / img.Width, 350.0 / img.Height);
            return scale >= 1 ? img.Width : img.Width * scale;
        }
    }

    [JsonIgnore]
    public double DisplayHeight
    {
        get
        {
            if (Metadata is not ImageFileMetadata img || img.Height == 0) return 350;
            var scale = Math.Min(400.0 / img.Width, 350.0 / img.Height);
            return scale >= 1 ? img.Height : img.Height * scale;
        }
    }
}
