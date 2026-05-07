using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;

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
) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    [JsonIgnore]
    public bool IsUploading
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public double UploadProgress
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public Bitmap? LocalPreviewBitmap
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public string RawUrl => $"{ApiClient.AutumnUrl}/{Tag}/{Id}/{Filename}";
    
    public string ThumbnailUrl => $"{RawUrl}?max_side=400";
    
    [JsonIgnore]
    public bool IsImage => ContentType?.StartsWith("image/") == true;
    
    [JsonIgnore]
    public double DisplayWidth
    {
        get
        {
            int width = Metadata is ImageFileMetadata img ? img.Width : (LocalPreviewBitmap?.PixelSize.Width ?? 400);
            int height = Metadata is ImageFileMetadata imgH ? imgH.Height : (LocalPreviewBitmap?.PixelSize.Height ?? 350);

            if (width == 0 || height == 0) return 400;

            var scale = Math.Min(400.0 / width, 350.0 / height);
            return scale >= 1 ? width : width * scale;
        }
    }

    [JsonIgnore]
    public double DisplayHeight
    {
        get
        {
            int width = Metadata is ImageFileMetadata img ? img.Width : (LocalPreviewBitmap?.PixelSize.Width ?? 400);
            int height = Metadata is ImageFileMetadata imgH ? imgH.Height : (LocalPreviewBitmap?.PixelSize.Height ?? 350);

            if (width == 0 || height == 0) return 350;

            var scale = Math.Min(400.0 / width, 350.0 / height);
            return scale >= 1 ? height : height * scale;
        }
    }
}
