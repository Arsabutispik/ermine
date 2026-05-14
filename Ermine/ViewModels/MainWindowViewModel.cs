    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Avalonia.Platform.Storage;
    using CommunityToolkit.Mvvm.ComponentModel;
    using CommunityToolkit.Mvvm.Messaging;
    using CommunityToolkit.Mvvm.Messaging.Messages;
    using Ermine.Models;
    using Serilog;
    using Velopack;       
    using Velopack.Sources; 

    namespace Ermine.ViewModels;

    public record LoginSuccessMessage(string Token, string UserId);

    public record LogoutMessage;
    
    public class PickFilesMessage(FilePickerOpenOptions options) 
        : AsyncRequestMessage<IReadOnlyList<IStorageFile>?>
    {
        public FilePickerOpenOptions Options { get; } = options;
    }

    public partial class MainWindowViewModel : ViewModelBase
    {
        [ObservableProperty] private ViewModelBase _currentPage;

        public MainWindowViewModel()
        {
            CurrentPage = new LoginViewModel();

            _ = CheckForSavedSessionAsync();
            
            _ = CheckForUpdatesInBackgroundAsync();

            WeakReferenceMessenger.Default.Register<LoginSuccessMessage>(this, (r, message) =>
            {
                CurrentPage = new MainChatViewModel(message.Token);
            });

            WeakReferenceMessenger.Default.Register<LogoutMessage>(this,
                (r, message) => { CurrentPage = new LoginViewModel(); });
        }

        private async Task CheckForSavedSessionAsync()
        {
            var apiClient = new ApiClient();
            var token = apiClient.TryLoadSavedSession();

            if (!string.IsNullOrEmpty(token))
                CurrentPage = new MainChatViewModel(token);
        }

        private async Task CheckForUpdatesInBackgroundAsync()
        {
            try
            {
                var source = new GithubSource("https://github.com/Arsabutispik/Ermine", null, false);
                var mgr = new UpdateManager(source);

                if (!mgr.IsInstalled)
                {
                    Log.Information("Velopack is not installed (likely running from IDE). Skipping update check.");
                    return;
                }

                Log.Information("Checking for Ermine updates...");
                
                var newVersion = await mgr.CheckForUpdatesAsync();
                if (newVersion == null)
                {
                    Log.Information("Ermine is up to date.");
                    return; 
                }

                Log.Information($"New version found: {newVersion.TargetFullRelease.Version}. Downloading in background...");

                await mgr.DownloadUpdatesAsync(newVersion);

                Log.Information("Update downloaded successfully. Applying...");

                mgr.WaitExitThenApplyUpdates(newVersion, silent: true, restart: false);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to check for or apply updates.");
            }
        }
    }