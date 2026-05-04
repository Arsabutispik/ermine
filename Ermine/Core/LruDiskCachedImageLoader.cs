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
            
            int maxChatDisplayWidth = 400; 
            bitmap = Bitmap.DecodeToWidth(fileStream, maxChatDisplayWidth);

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

    public void Dispose()
    {
        _rawHttp.Dispose();
        _lock.Dispose();
    }
}