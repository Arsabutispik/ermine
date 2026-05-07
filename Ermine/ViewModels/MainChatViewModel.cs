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
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Ermine.Core;
using Serilog;

namespace Ermine.ViewModels;

public partial class MainChatViewModel : ViewModelBase
{
    private GatewayClient? _gateway;

    private readonly Dictionary<string, Channel> _allChannels = new();

    private readonly Dictionary<string, string> _serverChannelMemory = new();
    
    private readonly Dictionary<string, ObservableCollection<Message>> _messageCache = new();
    private int _messageLoadVersion;
    private readonly Dictionary<string, bool> _hasMoreMessages = new();
    private readonly Dictionary<string, bool> _isFetchingOlder = new();
    public record PrependingMessagesNotification;
    public record PrependedMessagesNotification;
    
    private static string GetMimeType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };

    [ObservableProperty]
    public partial Server? SelectedServer { get; set; }

    [ObservableProperty]
    public partial User? CurrentUser { get; private set; }

    [ObservableProperty]
    public partial string DraftMessage { get; set; } = string.Empty;
    [ObservableProperty]
    public partial Channel? SelectedChannel { get; set; }
    
    public record ScrollToMessageRequest(Message Target);

    private bool _isRestoringState;
    [ObservableProperty]
    public partial Channel? SavedNotesChannel { get; set; }
    public ObservableCollection<Server> Servers { get; } = new();
    public ObservableCollection<Channel> DirectMessages { get; } = new();
    public ObservableCollection<ChannelGroup> ServerChannelGroups { get; set; } = new();
    public ObservableCollection<Message> CurrentMessages { get; set; } = new();
    
    [ObservableProperty]
    public partial ObservableCollection<StagedAttachment> StagedAttachments { get; set; } = new();

    public MainChatViewModel(string sessionToken)
    {
        InitializeGateway(sessionToken);
    }

    private void HandleLiveMessage(Message incomingMessage)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_messageCache.TryGetValue(incomingMessage.Channel, out var cached))
            {
                cached = new ObservableCollection<Message>();
                _messageCache[incomingMessage.Channel] = cached;
            }
            
            int existingIndex = -1;
            Message? pendingMsg = null;
            
            if (!string.IsNullOrEmpty(incomingMessage.Nonce))
            {
                existingIndex = cached.ToList().FindIndex(m => m.Nonce == incomingMessage.Nonce);
                if (existingIndex >= 0)
                {
                    pendingMsg = cached[existingIndex];
                }
            }
            
            if (incomingMessage.User == null && !string.IsNullOrEmpty(incomingMessage.Author))
            {
                var knownMessage = cached.FirstOrDefault(m => m.Author == incomingMessage.Author && m.User != null);
                if (knownMessage != null)
                {
                    incomingMessage = incomingMessage with { User = knownMessage.User };
                }
                else
                {
                    // TODO: Fetch profile from API as the cache doesn't know about it
                }
            }

            if (incomingMessage.User != null)
            {
                GlobalCache.Users[incomingMessage.User.Id] = incomingMessage.User;
            }

            if (incomingMessage.Replies?.Length > 0)
            {
                var resolvedReplies = new List<Message>();
                foreach (var replyId in incomingMessage.Replies)
                {
                    var targetMsg = cached.FirstOrDefault(m => m.Id == replyId);
                    if (targetMsg != null)
                    {
                        var isMention = incomingMessage.Mentions != null && incomingMessage.Mentions.Contains(targetMsg.Author);
                        resolvedReplies.Add(targetMsg with { IsMentionReply = isMention });
                    }
                }
            
                if (resolvedReplies.Count > 0)
                {
                    incomingMessage.ResolvedReplies = resolvedReplies;
                }
            }
            
            if (existingIndex >= 0 && pendingMsg != null)
            {
                if (pendingMsg.Attachments != null && incomingMessage.Attachments != null)
                {
                    for (int i = 0; i < Math.Min(pendingMsg.Attachments.Count, incomingMessage.Attachments.Count); i++)
                    {
                        incomingMessage.Attachments[i].LocalPreviewBitmap = pendingMsg.Attachments[i].LocalPreviewBitmap;
                    }
                }

                cached[existingIndex] = incomingMessage;

                if (SelectedChannel != null && incomingMessage.Channel == SelectedChannel.Id)
                {
                    if (!ReferenceEquals(CurrentMessages, cached))
                    {
                        var viewIndex = CurrentMessages.IndexOf(pendingMsg);
                        if (viewIndex >= 0) 
                        {
                            CurrentMessages[viewIndex] = incomingMessage;
                        }
                    }
                }
            }
            else
            {
                cached.Add(incomingMessage);

                if (SelectedChannel != null && incomingMessage.Channel == SelectedChannel.Id)
                {
                    if (!ReferenceEquals(CurrentMessages, cached))
                    {
                        CurrentMessages.Add(incomingMessage);
                    }
                }
            }
        });
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
        
        if (readyData.Emojis != null)
            foreach (var emoji in readyData.Emojis)
                GlobalCache.Emojis[emoji.Id] = emoji;
        if (readyData.Servers != null)
            foreach (var server in readyData.Servers)
                GlobalCache.Servers[server.Id] = server;

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
        if (string.IsNullOrWhiteSpace(DraftMessage) && StagedAttachments.Count == 0) return;
        if (SelectedChannel == null) return;

        var contentToSend = DraftMessage;
        var attachmentsToSend = StagedAttachments.ToList();

        DraftMessage = string.Empty;
        StagedAttachments.Clear();

        var pendingAttachments = new List<Attachment>();
        foreach (var staged in attachmentsToSend)
        {
            var pendingAttachment = new Attachment(
                Id: Guid.NewGuid().ToString(),
                ContentType: GetMimeType(staged.FileName),
                Filename: staged.FileName, 
                Metadata: null,
                Size: 0,
                Tag: "pending"
            )
            {
                IsUploading = true,
                UploadProgress = 0,
                LocalPreviewBitmap = staged.PreviewBitmap
            };

            pendingAttachments.Add(pendingAttachment);
        }
        
        string messageNonce = Guid.NewGuid().ToString();

        var pendingMessage = new Message(
            Id: $"pending-{Guid.NewGuid()}",
            Author: CurrentUser?.Id ?? "unknown",
            Channel: SelectedChannel.Id,
            Attachments: pendingAttachments,
            Content: contentToSend,
            Nonce: messageNonce,
            User: CurrentUser
        );

        pendingMessage.IsPending = true;

        CurrentMessages.Add(pendingMessage);

        try
        {
            var attachmentIds = new List<string>();

            for (int i = 0; i < attachmentsToSend.Count; i++)
            {
                var staged = attachmentsToSend[i];
                var uiAttachment = pendingAttachments[i];

                uiAttachment.UploadCts = new CancellationTokenSource();
                
                var progressReporter = new Progress<double>(percent => 
                {
                    uiAttachment.UploadProgress = percent; 
                });

                var id = await ApiClient.UploadAttachmentAsync(
                    staged.FileName,
                    staged.Data,
                    staged.MimeType,
                    progressReporter,
                    uiAttachment.UploadCts.Token);

                if (id != null)
                {
                    attachmentIds.Add(id);
                    uiAttachment.UploadProgress = 100;
                    await Task.Delay(200);
                    uiAttachment.IsUploading = false;
                }
            }

            await ApiClient.SendMessageAsync(SelectedChannel.Id, contentToSend, attachmentIds, messageNonce);

        }
        catch (Exception)
        {
            CurrentMessages.Remove(pendingMessage);

            DraftMessage = contentToSend;
            foreach (var a in attachmentsToSend)
            {
                StagedAttachments.Add(a);
            }
        }
    }

    private async Task LoadMessagesAsync(string channelId)
    {
        var loadVersion = ++_messageLoadVersion;

        if (_messageCache.TryGetValue(channelId, out var cached))
        {
            if (loadVersion != _messageLoadVersion) return;
            CurrentMessages = cached;
            OnPropertyChanged(nameof(CurrentMessages));
            return;
        }

        if (loadVersion == _messageLoadVersion)
        {
            CurrentMessages = new ObservableCollection<Message>();
            OnPropertyChanged(nameof(CurrentMessages));
        }

        var messages = await ApiClient.FetchMessagesAsync(channelId);
        if (loadVersion != _messageLoadVersion || SelectedChannel?.Id != channelId)
            return;

        if (messages == null || !messages.Any())
        {
            var emptyCollection = new ObservableCollection<Message>();
            _messageCache[channelId] = emptyCollection;
            CurrentMessages = emptyCollection;
            OnPropertyChanged(nameof(CurrentMessages));
            return;
        }

        messages.Reverse();

        var collection = new ObservableCollection<Message>(messages);
        _messageCache[channelId] = collection;
        _hasMoreMessages[channelId] = messages.Count >= 50;
        _isFetchingOlder[channelId] = false;

        CurrentMessages = collection;
        OnPropertyChanged(nameof(CurrentMessages));
    }
    
    public async Task FetchOlderMessagesAsync(string channelId)
    {
        if (_isFetchingOlder.GetValueOrDefault(channelId)) return;
        if (!_hasMoreMessages.GetValueOrDefault(channelId, true)) return;
        if (!_messageCache.TryGetValue(channelId, out var cached) || cached.Count == 0) return;

        _isFetchingOlder[channelId] = true;
        try
        {
            var oldestId = cached[0].Id;
            var messages = await ApiClient.FetchMessagesAsync(channelId, beforeId: oldestId);

            if (messages == null || messages.Count == 0)
            {
                _hasMoreMessages[channelId] = false;
                return;
            }

            if (SelectedChannel?.Id != channelId) return;

            Dispatcher.UIThread.Post(() =>
                WeakReferenceMessenger.Default.Send(new PrependingMessagesNotification()));

            messages.Reverse();
            for (int i = 0; i < messages.Count; i++)
                cached.Insert(i, messages[i]);

            Dispatcher.UIThread.Post(() =>
                WeakReferenceMessenger.Default.Send(new PrependedMessagesNotification()));

            if (messages.Count < 50)
                _hasMoreMessages[channelId] = false;    
        }
        finally
        {
            _isFetchingOlder[channelId] = false;
        }
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
        foreach (var channelId in SelectedServer.Channels)
        {
            if (!categorizedChannelIds.Contains(channelId) && TryGetChannelById(channelId, out Channel channel))
            {
                uncategorizedGroup.Channels.Add(channel);
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
    
    [RelayCommand]
    private async Task PickAttachmentAsync()
    {
        var files = await WeakReferenceMessenger.Default.Send(
            new PickFilesMessage(new FilePickerOpenOptions { AllowMultiple = true }));

        if (files == null) return;

        foreach (var file in files)
        {
            await using var stream = await file.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var data = ms.ToArray();

            var attachment = new StagedAttachment
            {
                FileName = file.Name,
                Data = data,
                MimeType = GetMimeType(file.Name)
            };
            attachment.GeneratePreview();
            StagedAttachments.Add(attachment);
            
        }
    }
    
    [RelayCommand]
    private void RemoveStagedAttachment(StagedAttachment attachment)
    {
        StagedAttachments.Remove(attachment);
    }
    
    public async Task StageFilesAsync(IEnumerable<IStorageFile> files)
    {
        foreach (var file in files)
        {
            await using var stream = await file.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);

            var attachment = new StagedAttachment
            {
                FileName = file.Name,
                Data = ms.ToArray(),
                MimeType = GetMimeType(file.Name)
            };
            attachment.GeneratePreview();
            StagedAttachments.Add(attachment);
        }
    }
    
    [RelayCommand]
    public void JumpToMessage(Message? targetMessage)
    {
        if (targetMessage == null) return;
    
        var actualMessageInFeed = CurrentMessages.FirstOrDefault(m => m.Id == targetMessage.Id);
        
        if (actualMessageInFeed != null)
        {
            WeakReferenceMessenger.Default.Send(new ScrollToMessageRequest(actualMessageInFeed));
        }
        else
        {
            // TODO: Fetch older messages from the API here if it's too far back
        }
    }
    
    [RelayCommand]
    private void CancelUpload(Attachment attachment)
    {
        attachment.UploadCts?.Cancel(); 
    }
}