using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Ermine.Models;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Ermine.Core;
using Serilog;

namespace Ermine.ViewModels;

public partial class MainChatViewModel : ViewModelBase
{
    private GatewayClient? _gateway;

    private Dictionary<string, Channel> _allChannels = new();
    
    private readonly Dictionary<string, string> _serverChannelMemory = new();

    [ObservableProperty] private Server? _selectedServer;
    [ObservableProperty] private User? _currentUser;
    [ObservableProperty]
    private string _draftMessage = string.Empty;
    [ObservableProperty] 
    private Channel? _selectedChannel;
    private bool _isRestoringState = false;
    
    public ObservableCollection<Server> Servers { get; } = new();
    public ObservableCollection<ChannelGroup> ServerChannelGroups { get; set; } = new();
    public ObservableCollection<Message> CurrentMessages { get; } = new();
    private readonly Dictionary<string, Bitmap> _avatarCache = new();

    private async Task<Bitmap?> GetAvatarAsync(string url)
    {
        if (_avatarCache.TryGetValue(url, out var cached))
            return cached;

        var bytes = await ApiClient.Http.GetByteArrayAsync(url);
        var bitmap = Bitmap.DecodeToWidth(new MemoryStream(bytes), 80);
        _avatarCache[url] = bitmap;
        return bitmap;
    }

    public MainChatViewModel(string sessionToken, GatewayClient gatewayClient)
    {
        InitializeGateway(sessionToken);
    }
    
    private async void HandleLiveMessage(Message incomingMessage)
    {
        if (SelectedChannel == null || incomingMessage.Channel != SelectedChannel.Id) 
            return;

        if (incomingMessage.DisplayAvatarUrl is { } url)
            incomingMessage.Avatar = await GetAvatarAsync(url);

        Dispatcher.UIThread.Post(() =>
        {
            CurrentMessages.Add(incomingMessage);
        });
    }
    private async void InitializeGateway(string token)
    {
        _gateway = new GatewayClient(token);
        _gateway.OnReady += HandleReadyEvent;
        _gateway.OnMessageReceived += HandleLiveMessage;
        
        await _gateway.StartAsync();
    }

    private void HandleReadyEvent(ReadyEvent readyData)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _allChannels.Clear();
            if (readyData.Channels != null)
            {
                foreach (var channel in readyData.Channels) _allChannels[channel.Id] = channel;
            }

            Servers.Clear();
            if (readyData.Servers != null)
            {
                foreach (var server in readyData.Servers) Servers.Add(server);
            }
            
            var me = readyData.Users?.FirstOrDefault(u => u.Relationship.ToString() == "User");
            if (me != null) CurrentUser = me;

            var settings = SettingsManager.Load();
            
            _isRestoringState = true;
            
            if (!string.IsNullOrEmpty(settings.LastServerId))
            {
                var previousServer = Servers.FirstOrDefault(s => s.Id == settings.LastServerId);
                if (previousServer != null)
                {
                    SelectedServer = previousServer; 

                    if (!string.IsNullOrEmpty(settings.LastChannelId) && 
                        _allChannels.TryGetValue(settings.LastChannelId, out var previousChannel))
                    {
                        _serverChannelMemory[previousServer.Id] = previousChannel.Id;
                        SelectedChannel = previousChannel;
                    }
                }
            }
            if (SelectedServer == null && Servers.Count > 0)
            {
                SelectedServer = Servers.First();
            
                SelectedChannel = ServerChannelGroups
                    .FirstOrDefault(group => group.Channels.Count > 0)?
                    .Channels.FirstOrDefault();
                
                if (SelectedChannel != null)
                {
                    _serverChannelMemory[SelectedServer.Id] = SelectedChannel.Id;
                }
            }
            _isRestoringState = false;
        });
    }

    [RelayCommand]
    private void SelectChannel(Channel channel)
    {
        SelectedChannel = channel;
        
        var settings = SettingsManager.Load();
        settings.LastServerId = SelectedServer?.Id;
        settings.LastChannelId = channel.Id;
        SettingsManager.Save(settings);
    }
    
    partial void OnSelectedServerChanged(Server? value)
    {
        LoadChannelsForSelectedServer();
        
        if (_isRestoringState) 
            return;
        
        if (value != null )
        {
            if (_serverChannelMemory.TryGetValue(value.Id, out var savedChannelId) && 
                TryGetChannelById(savedChannelId, out var channel))
            {
                SelectedChannel = channel;
            }
            else
            {
                SelectedChannel = ServerChannelGroups
                    .FirstOrDefault(group => group.Channels.Count > 0)?
                    .Channels.FirstOrDefault();
            }
        }
        else
        {
            SelectedChannel = null;
        }
    
        var settings = SettingsManager.Load();
        settings.LastServerId = value?.Id;
        settings.LastChannelId = SelectedChannel?.Id;
        SettingsManager.Save(settings);
    }
    
    partial void OnSelectedChannelChanged(Channel? value)
    {
        if (value != null)
        {
            if (SelectedServer != null)
            {
                _serverChannelMemory[SelectedServer.Id] = value.Id;
            }
            
            _ = LoadMessagesAsync(value.Id);
        }
        else
        {
            CurrentMessages.Clear();
        }
    }
    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(DraftMessage) || SelectedChannel == null)
            return;

        var contentToSend = DraftMessage;
        
        DraftMessage = string.Empty;

        try
        {
            await ApiClient.SendMessageAsync(SelectedChannel.Id, contentToSend);
        }
        catch (Exception)
        {
            DraftMessage = contentToSend; 
        }
    }
    private async Task LoadMessagesAsync(string channelId)
    {
        CurrentMessages.Clear();
    
        var messages = await ApiClient.FetchMessagesAsync(channelId);

        if (messages == null || !messages.Any()) return;
    
        messages.Reverse();

        // Load all avatars in parallel
        await Task.WhenAll(messages.Select(async msg =>
        {
            if (msg.DisplayAvatarUrl is { } url)
                msg.Avatar = await GetAvatarAsync(url);
        }));

        // Add all messages at once
        foreach (var msg in messages)
            CurrentMessages.Add(msg);
    }
    
    private void LoadChannelsForSelectedServer()
    {
        if (SelectedServer == null) 
        {
            ServerChannelGroups.Clear();
            return;
        }

        var groups = new ObservableCollection<ChannelGroup>();
        var categorizedChannelIds = new HashSet<string>();

        if (SelectedServer.Categories != null)
        {
            foreach (var category in SelectedServer.Categories)
            {
                var group = new ChannelGroup { CategoryName = category.Title };
            
                foreach (var channelId in category.Channels)
                {
                    if (TryGetChannelById(channelId, out Channel channel)) 
                    {
                        group.Channels.Add(channel);
                        categorizedChannelIds.Add(channelId);
                    }
                }
            
                if (group.Channels.Count > 0)
                    groups.Add(group);
            }
        }

        var uncategorizedGroup = new ChannelGroup { CategoryName = null };
        if (SelectedServer.Channels != null)
        {
            foreach (var channelId in SelectedServer.Channels)
            {
                if (!categorizedChannelIds.Contains(channelId))
                {
                    if (TryGetChannelById(channelId, out Channel channel))
                    {
                        uncategorizedGroup.Channels.Add(channel);
                    }
                }
            }
        }

        if (uncategorizedGroup.Channels.Count > 0)
        {
            groups.Insert(0, uncategorizedGroup);
        }

        ServerChannelGroups = groups;
        
        OnPropertyChanged(nameof(ServerChannelGroups)); 
    }
    private bool TryGetChannelById(string channelId, out Channel channel)
    {
        return _allChannels.TryGetValue(channelId, out channel!);
    }
    
    [RelayCommand]
    private void Logout()
    {
        var apiClient = new ApiClient();
        apiClient.ClearSession();
        
        WeakReferenceMessenger.Default.Send(new LogoutMessage());
    }
}