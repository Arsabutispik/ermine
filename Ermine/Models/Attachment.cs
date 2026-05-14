using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Threading;
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
            OnPropertyChanged(nameof(DisplayWidth));
            OnPropertyChanged(nameof(DisplayHeight));
        }
    }

    [JsonIgnore]
    public string RawUrl => $"{ApiClient.AutumnUrl}/{Tag}/{Id}/{Filename}";
    
    public string ThumbnailUrl => $"{ApiClient.AutumnUrl}/{Tag}/{Id}";
    
    [JsonIgnore]
    public bool IsImage => ContentType?.StartsWith("image/") == true;
    
    public double DisplayWidth
    {
        get
        {
            if (Metadata is not ImageFileMetadata img) 
                return LocalPreviewBitmap?.PixelSize.Width ?? 400;
        
            if (img.Width == 0 || img.Height == 0) return 400;
        
            var maxSide = Math.Max(img.Width, img.Height);
            var scale = maxSide > 400 ? 400.0 / maxSide : 1.0;
            return Math.Round(img.Width * scale);
        }
    }

    public double DisplayHeight
    {
        get
        {
            if (Metadata is not ImageFileMetadata img)
                return LocalPreviewBitmap?.PixelSize.Height ?? 350;
        
            if (img.Width == 0 || img.Height == 0) return 350;
        
            var maxSide = Math.Max(img.Width, img.Height);
            var scale = maxSide > 400 ? 400.0 / maxSide : 1.0;
            return Math.Round(img.Height * scale);
        }
    }
    
    [JsonIgnore]
    public CancellationTokenSource? UploadCts { get; set; }
}
