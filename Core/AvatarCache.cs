using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

public class AvatarCache
{
    private readonly ConcurrentDictionary<string, Bitmap> _cache = new();
    private readonly HttpClient _http = new();

    public static AvatarCache Instance { get; } = new();

    public async Task<Bitmap?> GetAsync(string url)
    {
        if (_cache.TryGetValue(url, out var cached))
            return cached;

        try
        {
            var bytes = await _http.GetByteArrayAsync(url);
            var bitmap = new Bitmap(new MemoryStream(bytes));
            _cache.TryAdd(url, bitmap);
            return bitmap;
        }
        catch { return null; }
    }
}