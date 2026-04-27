using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Ermine.Models;

namespace Ermine.ViewModels;

// We use a simple record as a message to signal a successful login
public record LoginSuccessMessage(string Token, string UserId);

public record LogoutMessage;

public partial class MainWindowViewModel : ViewModelBase
{
    // This holds whatever screen the user should currently see
    [ObservableProperty] private ViewModelBase _currentPage;

    public MainWindowViewModel()
    {
        // Start by assuming we need to log in
        CurrentPage = new LoginViewModel();

        // Check for a saved session in the background
        _ = CheckForSavedSessionAsync();

        WeakReferenceMessenger.Default.Register<LoginSuccessMessage>(this, (r, message) =>
        {
            // Pass the token from the message into the new ViewModel
            CurrentPage = new MainChatViewModel(message.Token, new GatewayClient(message.Token));
        });

        WeakReferenceMessenger.Default.Register<LogoutMessage>(this,
            (r, message) => { CurrentPage = new LoginViewModel(); });
    }

    private async Task CheckForSavedSessionAsync()
    {
        var apiClient = new ApiClient();
        var token = apiClient
            .TryLoadSavedSession();
        var gatewayClient = new GatewayClient(token);

        if (!string.IsNullOrEmpty(token))
            // Skip straight to the main UI, passing the saved token
            CurrentPage = new MainChatViewModel(token, gatewayClient);
    }
}