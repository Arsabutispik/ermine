using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Ermine.Helpers;

public class ProgressHttpContent : HttpContent
{
    private readonly HttpContent _innerContent;
    private readonly IProgress<double> _progress;
    private readonly int _bufferSize;

    public ProgressHttpContent(HttpContent innerContent, IProgress<double> progress, int bufferSize = 8192)
    {
        _innerContent = innerContent;
        _progress = progress;
        _bufferSize = bufferSize;
        
        foreach (var header in innerContent.Headers)
        {
            Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        using var innerStream = await _innerContent.ReadAsStreamAsync();
        var buffer = new byte[_bufferSize];
        long totalBytes = _innerContent.Headers.ContentLength ?? innerStream.Length;
        long uploadedBytes = 0;

        while (true)
        {
            int bytesRead = await innerStream.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead == 0) break; 

            await stream.WriteAsync(buffer, 0, bytesRead);
            uploadedBytes += bytesRead;

            if (totalBytes > 0)
            {
                double percent = Math.Round((double)uploadedBytes / totalBytes * 100);
                _progress.Report(percent);
            }
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _innerContent.Headers.ContentLength ?? -1;
        return length != -1;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _innerContent.Dispose();
        }
        base.Dispose(disposing);
    }
}