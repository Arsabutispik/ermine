using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Ermine.Models;

public partial class StagedAttachment : ObservableObject
{
    public string FileName { get; set; } = string.Empty;
    public byte[] Data { get; set; } = [];
    public string MimeType { get; set; } = "application/octet-stream";
    
    private Bitmap? _previewBitmap;
    public Bitmap? PreviewBitmap 
    { 
        get => _previewBitmap;
        set => SetProperty(ref _previewBitmap, value);
    }

    public bool IsImage => MimeType.StartsWith("image/");

    public void GeneratePreview()
    {
        if (!IsImage || Data.Length == 0) return;

        try
        {
            using var ms = new MemoryStream(Data);
            PreviewBitmap = Bitmap.DecodeToWidth(ms, 200);
        }
        catch
        {
            PreviewBitmap = null;
        }
    }
}