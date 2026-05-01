using System.Threading.Tasks;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Ermine.Models;

namespace Ermine.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient = new();

    [ObservableProperty] private string _email = string.Empty;

    [ObservableProperty] private bool _isError;

    [ObservableProperty] private bool _isLoggingIn;

    [ObservableProperty] private string _instanceUrl = string.Empty;

    [ObservableProperty] private ObservableCollection<string> _savedInstanceUrls = new();

    public bool HasSavedInstanceUrls => SavedInstanceUrls.Count > 0;

    private string? _selectedInstanceUrl;
    public string? SelectedInstanceUrl
    {
        get => _selectedInstanceUrl;
        set
        {
            SetProperty(ref _selectedInstanceUrl, value);
            if (!string.IsNullOrEmpty(value))
            {
                InstanceUrl = value;
            }
        }
    }

    public LoginViewModel()
    {
        SavedInstanceUrls.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasSavedInstanceUrls));

        if (System.IO.File.Exists("instances.txt"))
        {
            var lines = System.IO.File.ReadAllLines("instances.txt");
            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line)) SavedInstanceUrls.Add(line);
            }
        }

        if (System.IO.File.Exists("instance.txt"))
        {
            InstanceUrl = System.IO.File.ReadAllText("instance.txt");
        }
    }

    [RelayCommand]
    private void RemoveInstanceUrl()
    {
        if (SavedInstanceUrls.Contains(InstanceUrl))
        {
            SavedInstanceUrls.Remove(InstanceUrl);
            System.IO.File.WriteAllLines("instances.txt", SavedInstanceUrls);
            InstanceUrl = string.Empty;
            SelectedInstanceUrl = null;
        }
    }

    [RelayCommand]
    private void RemoveSpecificInstanceUrl(string urlToRemove)
    {
        if (!string.IsNullOrEmpty(urlToRemove) && SavedInstanceUrls.Contains(urlToRemove))
        {
            SavedInstanceUrls.Remove(urlToRemove);
            System.IO.File.WriteAllLines("instances.txt", SavedInstanceUrls);
            if (InstanceUrl == urlToRemove)
            {
                InstanceUrl = string.Empty;
            }
        }
    }

    // --- NEW MFA STATE ---
    [ObservableProperty] private bool _isMfaRequired;
    [ObservableProperty] private string _password = string.Empty;
    private string _pendingMfaTicket = string.Empty;
    [ObservableProperty] private string _statusMessage = "Awaiting Login...";
    [ObservableProperty] private string _totpCode = string.Empty;

    [RelayCommand]
    private async Task AttemptLoginAsync()
    {
        IsLoggingIn = true;
        StatusMessage = "Authenticating...";

        if (!string.IsNullOrWhiteSpace(InstanceUrl))
        {
            if (!InstanceUrl.StartsWith("http://") && !InstanceUrl.StartsWith("https://"))
            {
                StatusMessage = "URL must start with http:// or https://";
                IsError = true;
                IsLoggingIn = false;
                return;
            }
            
            try
            {
                var uri = new System.Uri(InstanceUrl);
            }
            catch (System.UriFormatException)
            {
                StatusMessage = "Invalid Server URL format.";
                IsError = true;
                IsLoggingIn = false;
                return;
            }

            ApiClient.UpdateInstanceUrl(InstanceUrl);
            System.IO.File.WriteAllText("instance.txt", InstanceUrl);
            if (!SavedInstanceUrls.Contains(InstanceUrl))
            {
                SavedInstanceUrls.Add(InstanceUrl);
                System.IO.File.WriteAllLines("instances.txt", SavedInstanceUrls);
            }
        }
        else
        {
            ApiClient.UpdateInstanceUrl("https://stoat.chat/api/");
            if (System.IO.File.Exists("instance.txt")) System.IO.File.Delete("instance.txt");
        }

        try
        {
            var result = await _apiClient.LoginWithCredentialsAsync(Email, Password);

            switch (result.Result)
            {
                case LoginResultType.Success:
                    FinalizeLogin(result.Session!);
                    break;

                case LoginResultType.MfaRequired:
                    _pendingMfaTicket = result.MfaTicket!;
                    IsMfaRequired = true;
                    StatusMessage = "Two-Factor Authentication required.";
                    break;

                case LoginResultType.AccountDisabled:
                    StatusMessage = "This account has been disabled. Please contact support.";
                    IsError = true;
                    break;
                case LoginResultType.Unauthorized:
                    StatusMessage = "Invalid email or password.";
                    IsError = true;
                    break;

                default:
                    StatusMessage = "Failed to connect to the server.";
                    IsError = true;
                    break;
            }
        }
        catch (System.Exception ex)
        {
            StatusMessage = "Network error: Make sure the URL is correct.";
            IsError = true;
            Serilog.Log.Error(ex, "Failed to reach instance API at {Url}", InstanceUrl);
        }

        IsLoggingIn = false;
    }

    [RelayCommand]
    private async Task SubmitMfaAsync()
    {
        IsLoggingIn = true;
        StatusMessage = "Verifying code...";

        var session = await _apiClient.SubmitMfaAsync(_pendingMfaTicket, TotpCode);

        if (session != null)
            FinalizeLogin(session);
        else
            StatusMessage = "Invalid Authenticator Code.";

        IsLoggingIn = false;
    }

    private void FinalizeLogin(SessionResponse session)
    {
        StatusMessage = "Success! Token acquired.";
        _apiClient.SaveSession(session.Token);
        WeakReferenceMessenger.Default.Send(new LoginSuccessMessage(session.Token, session.UserId));
    }
}