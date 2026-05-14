using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Serilog;
using Velopack;
using Velopack.Sources;

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
            var mgr = new UpdateManager(new GithubSource("https://github.com/Arsabutispik/ermine", null, false));
        
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
            mgr.WaitExitThenApplyUpdates(info);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update check failed.");
        }
    }
}