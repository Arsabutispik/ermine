using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Serilog;
using Velopack;

namespace Ermine.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        await CheckForUpdatesAsync();
    }
    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var mgr = new UpdateManager("https://github.com/Arsabutispik/ermine");
        
            if (!mgr.IsInstalled)
            {
                Log.Debug("App not installed via Velopack, skipping update check.");
                return;
            }

            var info = await mgr.CheckForUpdatesAsync();
            if (info == null)
            {
                Log.Information("No updates available.");
                return;
            }

            Log.Information("Update available: {Version}", info.TargetFullRelease.Version);
            await mgr.DownloadUpdatesAsync(info);
            mgr.ApplyUpdatesAndRestart(info);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update check failed.");
        }
    }
}