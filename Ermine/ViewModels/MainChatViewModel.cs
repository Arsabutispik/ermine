using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Ermine.Models;
using System.Linq;
using System.Threading.Tasks;
using Ermine.Core;
using Serilog;

namespace Ermine.ViewModels;

public partial class MainChatViewModel : ViewModelBase
{
    private GatewayClient? _gateway;

    private readonly Dictionary<string, Channel> _allChannels = new();

    private readonly Dictionary<string, string> _serverChannelMemory = new();
    
    private readonly Dictionary<string, ObservableCollection<Message>> _messageCache = new();

    [ObservableProperty]
    public partial Server? SelectedServer { get; set; }

    [ObservableProperty]
    public partial User? CurrentUser { get; private set; }

    [ObservableProperty]
    public partial string DraftMessage { get; set; } = string.Empty;
    [ObservableProperty]
    public partial Channel? SelectedChannel { get; set; }

    private bool _isRestoringState;
    [ObservableProperty]
    public partial Channel? SavedNotesChannel { get; set; }
    public ObservableCollection<Server> Servers { get; } = new();
    public ObservableCollection<Channel> DirectMessages { get; } = new();
    public ObservableCollection<ChannelGroup> ServerChannelGroups { get; set; } = new();
    public ObservableCollection<Message> CurrentMessages { get; set; } = new();

    public MainChatViewModel(string sessionToken)
    {
        InitializeGateway(sessionToken);
    }

    private void HandleLiveMessage(Message incomingMessage)
    {
        if (SelectedChannel == null || incomingMessage.Channel != SelectedChannel.Id)
            return;

        Dispatcher.UIThread.Post(() => { CurrentMessages.Add(incomingMessage); });
    }

    private async void InitializeGateway(string token)
    {
        try
        {
            _gateway = new GatewayClient(token);
            _gateway.OnReady += HandleReadyEvent;
            _gateway.OnMessageReceived += HandleLiveMessage;

            await _gateway.StartAsync();
        }
        catch (Exception e)
        {
            Log.Error(e, "Gateway connection failed, retrying once");
            try
            {
                await Task.Delay(2000);
                await _gateway!.StartAsync();
            }
            catch (Exception e2)
            {
                Log.Error(e2, "Gateway reconnect failed, logging out");
                Dispatcher.UIThread.Post(() =>
                    WeakReferenceMessenger.Default.Send(new LogoutMessage()));
            }
        }
    }

    private void HandleReadyEvent(ReadyEvent readyData)
{
    Dispatcher.UIThread.InvokeAsync(() =>
    {
        _allChannels.Clear();
        DirectMessages.Clear();

        if (readyData.Channels != null)
        {
            foreach (var channel in readyData.Channels)
                _allChannels[channel.Id] = channel;

            var dmChannels = readyData.Channels
                .Where(c => c is Group or SavedMessagesChannel or DirectMessageChannel { Active: true })
                .OrderByDescending(c => c switch
                {
                    DirectMessageChannel dm => dm.LastMessageId ?? string.Empty,
                    Group g => g.LastMessageId ?? string.Empty,
                    _ => string.Empty
                });

            SavedNotesChannel = readyData.Channels.FirstOrDefault(c => c is SavedMessagesChannel);

            foreach (var channel in dmChannels)
                DirectMessages.Add(channel);
        }

        Servers.Clear();
        if (readyData.Servers != null)
            foreach (var server in readyData.Servers)
                Servers.Add(server);

        var me = readyData.Users?.FirstOrDefault(u => u.Relationship == Relationship.User);
        if (me != null)
        {
            CurrentUser = me;
            GlobalCache.CurrentUserId = me.Id;
        }

        if (readyData.Users != null)
            foreach (var user in readyData.Users)
                GlobalCache.Users[user.Id] = user;

        _isRestoringState = true;

        var settings = SettingsManager.Load();

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
                else
                {
                    SelectedChannel = ServerChannelGroups
                        .FirstOrDefault(g => g.Channels.Count > 0)?
                        .Channels.FirstOrDefault();
                }
            }
        }

        if (SelectedServer == null)
        {
            if (Servers.Count > 0)
            {
                SelectedServer = Servers.First();

                SelectedChannel = ServerChannelGroups
                    .FirstOrDefault(g => g.Channels.Count > 0)?
                    .Channels.FirstOrDefault();

                if (SelectedChannel != null)
                    _serverChannelMemory[SelectedServer.Id] = SelectedChannel.Id;
            }
            else
            {
                SelectedServer = null;
                SelectedChannel = DirectMessages.FirstOrDefault();
            }
        }

        _isRestoringState = false;

        settings.LastServerId = SelectedServer?.Id;
        settings.LastChannelId = SelectedChannel?.Id;
        SettingsManager.Save(settings);
    });
}

    [RelayCommand]
    private void SelectHome()
    {
        SelectedServer = null;
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

        if (value != null)
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
        if (_messageCache.TryGetValue(channelId, out var cached))
        {
            CurrentMessages = cached;
            OnPropertyChanged(nameof(CurrentMessages));
            return;
        }

        CurrentMessages.Clear();

        var messages = await ApiClient.FetchMessagesAsync(channelId);
        if (messages == null || !messages.Any()) return;

        messages.Reverse();

        var collection = new ObservableCollection<Message>(messages);
        _messageCache[channelId] = collection;

        CurrentMessages = collection;
        OnPropertyChanged(nameof(CurrentMessages));
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