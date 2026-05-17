using System;
using System.IO;
using AsyncImageLoader;
using AsyncImageLoader.Loaders;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Ermine.Core;
using Ermine.ViewModels;
using Ermine.Views;

namespace Ermine;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public static LruDiskCachedImageLoader ImageCache { get; private set; } = null!;
        
    public override void OnFrameworkInitializationCompleted()
    {
        var cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ermine",
            "ImageCache");

        var loader = new LruDiskCachedImageLoader(cacheDirectory, maxBytes: 150L * 1024 * 1024);
        ImageCache = loader;
        ImageLoader.AsyncImageLoader = loader;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };

        base.OnFrameworkInitializationCompleted();
    }
}