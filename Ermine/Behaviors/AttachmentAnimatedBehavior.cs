using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Ermine.Behaviors;

public class AttachmentAnimatedBehavior
{
    public static readonly AttachedProperty<string?> AnimatedSourceUrlProperty =
        AvaloniaProperty.RegisterAttached<AttachmentAnimatedBehavior, Image, string?>("AnimatedSourceUrl");

    private static readonly ConcurrentDictionary<Image, CancellationTokenSource> Running = new();

    static AttachmentAnimatedBehavior()
    {
        AnimatedSourceUrlProperty.Changed.AddClassHandler<Image>(OnAnimatedSourceChanged);
    }

    public static string? GetAnimatedSourceUrl(Image img) => img.GetValue(AnimatedSourceUrlProperty);
    public static void SetAnimatedSourceUrl(Image img, string? value) => img.SetValue(AnimatedSourceUrlProperty, value);

    private static void OnAnimatedSourceChanged(Image img, AvaloniaPropertyChangedEventArgs e)
    {
        if (Running.TryRemove(img, out var prevCts))
        {
            try { prevCts.Cancel(); prevCts.Dispose(); }
            catch
            {
                // ignored
            }
        }

        var url = e.NewValue as string;
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        var cts = new CancellationTokenSource();
        Running[img] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                var frames = await App.ImageCache.ProvideFramesAsync(url);
                if (frames == null || frames.Count == 0)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        img[AsyncImageLoader.ImageLoader.SourceProperty] = url;
                    });
                    return;
                }

                if (cts.IsCancellationRequested) return;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    img.Source = frames[0].Bitmap;
                });

                if (frames.Count <= 1) return;

                int index = 0;

                async void Tick()
                {
                    if (cts.IsCancellationRequested) return;
                    index = (index + 1) % frames.Count;
                    await Dispatcher.UIThread.InvokeAsync(() => img.Source = frames[index].Bitmap);
                    try
                    {
                        await Task.Delay(frames[index].DelayMs, cts.Token);
                    }
                    catch (TaskCanceledException) { return; }
                    Tick();
                }

                try
                {
                    await Task.Delay(frames[0].DelayMs, cts.Token);
                }
                catch (TaskCanceledException) { return; }

                Tick();
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                await Dispatcher.UIThread.InvokeAsync(() => img[AsyncImageLoader.ImageLoader.SourceProperty] = url);
            }
        }, cts.Token);
    }
}




