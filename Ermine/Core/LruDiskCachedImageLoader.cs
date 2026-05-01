using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AsyncImageLoader;
using AsyncImageLoader.Loaders;
using Avalonia.Media.Imaging;

public class LruDiskCachedImageLoader : IAsyncImageLoader
{
    private readonly BaseWebImageLoader _http = new();
    private readonly LruCache<string, Bitmap> _ramCache;
    private readonly string _cacheDir;
    private readonly Dictionary<string, Task<Bitmap?>> _inFlight = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public LruDiskCachedImageLoader(string cacheDir, int maxEntries = 150)
    {
        _cacheDir = cacheDir;
        _ramCache = new LruCache<string, Bitmap>(maxEntries);
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

        if (File.Exists(diskPath))
        {
            bitmap = Bitmap.DecodeToWidth(File.OpenRead(diskPath), 80);
        }
        else
        {
            bitmap = await _http.ProvideImageAsync(url);
            if (bitmap != null)
                await SaveToDiskAsync(bitmap, diskPath);
        }

        if (bitmap != null)
            _ramCache.Add(url, bitmap);

        return bitmap;
    }

    private string GetDiskPath(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        return Path.Combine(_cacheDir, hash + ".png");
    }

    private static async Task SaveToDiskAsync(Bitmap bitmap, string path)
    {
        await Task.Run(() => bitmap.Save(path));
    }

    public void Dispose() => _http.Dispose();
}