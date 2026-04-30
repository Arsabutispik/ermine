using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AsyncImageLoader;
using AsyncImageLoader.Loaders;
using Avalonia.Media.Imaging;

public class LruDiskCachedImageLoader : IAsyncImageLoader
{
    private readonly BaseWebImageLoader _http = new();
    private readonly LruCache<string, Bitmap> _ramCache;
    private readonly string _cacheDir;

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

        var diskPath = GetDiskPath(url);

        Bitmap? bitmap = null;

        if (File.Exists(diskPath))
        {
            bitmap = new Bitmap(diskPath);
        }
        else
        {
            // Download via BaseWebImageLoader (no internal cache)
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
        return Path.Combine(_cacheDir, hash);
    }

    private static async Task SaveToDiskAsync(Bitmap bitmap, string path)
    {
        await Task.Run(() => bitmap.Save(path));
    }

    public void Dispose() => _http.Dispose();
}