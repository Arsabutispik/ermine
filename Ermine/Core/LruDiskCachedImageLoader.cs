using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AsyncImageLoader;
using Avalonia.Media.Imaging;
using ImageMagick;
using Serilog;

namespace Ermine.Core;

public class LruDiskCachedImageLoader : IAsyncImageLoader
{
    private readonly LruCache<string, Bitmap> _ramCache;
    private readonly string _cacheDir;
    private readonly int _decodeWidth;
    private readonly bool _hasDecodeWidth;
    private readonly Dictionary<string, Task<Bitmap?>> _inFlight = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly HttpClient _rawHttp = new();
    private readonly Dictionary<string, List<(WriteableBitmap Bitmap, int DelayMs)>> _frameCache = new();
    
    public LruDiskCachedImageLoader(string cacheDir, long maxBytes = 150L * 1024 * 1024, int? decodeWidth = null)
    {
        _cacheDir = cacheDir;
        _hasDecodeWidth = decodeWidth.HasValue && decodeWidth.Value > 0;
        _decodeWidth = decodeWidth.GetValueOrDefault();
        
        _ramCache = new LruCache<string, Bitmap>(
            maxBytes, 
            bmp => bmp.PixelSize.Width * bmp.PixelSize.Height * 4
        );
        
        Directory.CreateDirectory(cacheDir);
    }

    public async Task<Bitmap?> ProvideImageAsync(string url)
    {
        if (_ramCache.TryGetValue(url, out var cached))
            return cached;

        await _lock.WaitAsync();
        Task<Bitmap?> task;
        if (_inFlight.TryGetValue(url, out var existing))
        {
            _lock.Release();
            task = existing;
        }
        else
        {
            task = LoadAsync(url);
            _inFlight[url] = task;
            _lock.Release();
        }

        var result = await task;

        await _lock.WaitAsync();
        _inFlight.Remove(url);
        _lock.Release();

        return result;
    }

    private async Task<Bitmap?> LoadAsync(string url)
    {
        var diskPath = GetDiskPath(url);
        Bitmap? bitmap = null;

        try
        {
            if (!File.Exists(diskPath))
            {
                var bytes = await _rawHttp.GetByteArrayAsync(url);
                using var writeStream = new FileStream(diskPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await writeStream.WriteAsync(bytes);
            }

            using var fileStream = new FileStream(diskPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            
            if (_hasDecodeWidth)
            {
                bitmap = Bitmap.DecodeToWidth(fileStream, _decodeWidth);
            }
            else
            {
                bitmap = new Bitmap(fileStream);
            }

            if (bitmap != null)
                _ramCache.Add(url, bitmap);

            return bitmap;
        }
        catch
        {
            if (File.Exists(diskPath))
                File.Delete(diskPath);
            bitmap?.Dispose();
            return null;
        }
    }
    
    private string GetDiskPath(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        return Path.Combine(_cacheDir, hash + ".bin");
    }
    
    public async Task<List<(WriteableBitmap Bitmap, int DelayMs)>?> ProvideFramesAsync(string url)
    {
        if (_frameCache.TryGetValue(url, out var cached))
            return cached;

        var uri = await ProvideGifDiskPathAsync(url);
        if (uri == null) return null;

        using var collection = new MagickImageCollection(uri.LocalPath);
        collection.Coalesce();

        var frames = new List<(WriteableBitmap, int)>();
        foreach (var frame in collection)
            frames.Add((MagickToBitmap(frame), Math.Max(20, (int)frame.AnimationDelay * 10)));

        _frameCache[url] = frames;
        return frames;
    }
    
    private static WriteableBitmap MagickToBitmap(IMagickImage<byte> frame)
    {
        var bmp = new WriteableBitmap(
            new Avalonia.PixelSize((int)frame.Width, (int)frame.Height),
            new Avalonia.Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Unpremul);

        using var fb = bmp.Lock();
        var bytes = frame.ToByteArray(MagickFormat.Bgra);
        System.Runtime.InteropServices.Marshal.Copy(bytes, 0, fb.Address, bytes.Length);
        return bmp;
    }
    
    public async Task<Uri?> ProvideGifDiskPathAsync(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
    
        foreach (var ext in new[] { ".gif", ".webp", ".png" })
        {
            var existingPath = Path.Combine(_cacheDir, hash + ext);
            if (File.Exists(existingPath))
                return new Uri(existingPath);
        }

        try
        {
            var response = await _rawHttp.GetAsync(url);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var ext = contentType switch
            {
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                "image/png" => ".png",
                _ => ".gif"
            };
        
        
            var diskPath = Path.Combine(_cacheDir, hash + ext);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            await File.WriteAllBytesAsync(diskPath, bytes);

            if (ext != ".gif")
            {
                try
                {
                    var gifPath = Path.Combine(_cacheDir, hash + ".gif");
                    using var ms = new MemoryStream(bytes);
                    using var collection = new MagickImageCollection(ms);
                    collection.Coalesce();
                    collection.Write(gifPath);
                    return new Uri(gifPath);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to convert downloaded image to GIF for {Url}", url);
                    return new Uri(diskPath);
                }
            }

            return new Uri(diskPath);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _rawHttp.Dispose();
        _lock.Dispose();
    }
}