using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Messaging;
using Ermine.ViewModels;
using Serilog;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Ermine.Models;

namespace Ermine.Views;

public partial class MainChatView : UserControl
{
    private MainChatViewModel? _boundViewModel;
    private ObservableCollection<Message>? _boundMessages;
    private ScrollViewer? _messageScrollViewer;
    private bool _stickToBottom = true;
    public MainChatView()
    {
        InitializeComponent();  
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        var textBox = this.FindControl<TextBox>("InputTextBox");
        textBox?.AddHandler(KeyDownEvent, OnTextBoxKeyDown, RoutingStrategies.Tunnel);
        WeakReferenceMessenger.Default.Register<PickFilesMessage>(this, (_, msg) =>
        {
            msg.Reply(PickFilesAsync(msg.Options));
        });
        
        WeakReferenceMessenger.Default.Register<MainChatViewModel.ScrollToMessageRequest>(this, (_, msg) =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                MessageList.ScrollIntoView(msg.Target);
        
                _stickToBottom = false; 
            });
        });
        MessageList.ContainerClearing += (s, e) =>
        {
            var imageControls = e.Container.GetVisualDescendants().OfType<Image>();
            foreach (var imageControl in imageControls)
            {
                AsyncImageLoader.ImageLoader.SetSource(imageControl, null);
                imageControl.Source = null; 
            }
        };
    }
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        AttachToViewModel();
        AttachToMessageScrollViewer();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        DetachFromMessageScrollViewer();
        DetachFromViewModel();
        base.OnUnloaded(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        AttachToViewModel();
    }
    private CancellationTokenSource? _scrollCts;

    private void AttachToViewModel()
    {
        if (DataContext is not MainChatViewModel vm)
            return;

        if (ReferenceEquals(_boundViewModel, vm))
        {
            AttachToMessages(vm.CurrentMessages);
            return;
        }

        DetachFromViewModel();
        _boundViewModel = vm;
        _boundViewModel.PropertyChanged += OnViewModelPropertyChanged;
        AttachToMessages(vm.CurrentMessages);
    }

    private void DetachFromViewModel()
    {
        if (_boundViewModel != null)
        {
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _boundViewModel = null;
        }

        DetachFromMessages();
    }

    private void AttachToMessages(ObservableCollection<Message>? messages)
    {
        if (ReferenceEquals(_boundMessages, messages))
            return;

        DetachFromMessages();
        _boundMessages = messages;

        if (_boundMessages != null)
            _boundMessages.CollectionChanged += OnMessagesChanged;
    }

    private void DetachFromMessages()
    {
        if (_boundMessages != null)
        {
            _boundMessages.CollectionChanged -= OnMessagesChanged;
            _boundMessages = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainChatViewModel.CurrentMessages) || _boundViewModel == null)
            return;

        AttachToMessages(_boundViewModel.CurrentMessages);
        _stickToBottom = true;
        ScrollToBottomAfterLayout(force: true);
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;

        _stickToBottom = true;
        ScrollToBottomAfterLayout(force: false);
    }

    private void AttachToMessageScrollViewer()
    {
        var scrollViewer = MessageList.FindDescendantOfType<ScrollViewer>();
        if (ReferenceEquals(_messageScrollViewer, scrollViewer))
            return;

        DetachFromMessageScrollViewer();
        _messageScrollViewer = scrollViewer;

        if (_messageScrollViewer != null)
            _messageScrollViewer.ScrollChanged += OnMessageScrollChanged;
    }

    private void DetachFromMessageScrollViewer()
    {
        if (_messageScrollViewer != null)
        {
            _messageScrollViewer.ScrollChanged -= OnMessageScrollChanged;
            _messageScrollViewer = null;
        }
    }

    private void OnMessageScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_messageScrollViewer == null)
            return;

        var isAtBottom = _messageScrollViewer.Offset.Y >= (_messageScrollViewer.Extent.Height - _messageScrollViewer.Viewport.Height - 20);

        if (isAtBottom)
        {
            _stickToBottom = true;
            return;
        }

        if (e.OffsetDelta.Y < 0)
            _stickToBottom = false;
    }

    private void ScrollToBottomAfterLayout(bool force)
    {
        var scrollViewer = _messageScrollViewer ?? MessageList.FindDescendantOfType<ScrollViewer>();
        if (scrollViewer == null) return;

        if (!force && !_stickToBottom)
        {
            var isAtBottom = scrollViewer.Offset.Y >= (scrollViewer.Extent.Height - scrollViewer.Viewport.Height - 50);
            if (!isAtBottom && scrollViewer.Extent.Height != 0) return;
        }

        _scrollCts?.Cancel();
        _scrollCts = new CancellationTokenSource();
        var token = _scrollCts.Token;

        _ = ScrollToBottomAfterLayoutAsync(scrollViewer, token);
    }

    private static async Task ScrollToBottomAfterLayoutAsync(ScrollViewer scrollViewer, CancellationToken token)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            if (!token.IsCancellationRequested)
                scrollViewer.ScrollToEnd();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while scrolling message list to bottom");
        }
    }
    private async Task<IReadOnlyList<IStorageFile>?> PickFilesAsync(FilePickerOpenOptions options)
    {
        try
        {
            var window = TopLevel.GetTopLevel(this);
            if (window?.StorageProvider is { } sp)
            {
                return await sp.OpenFilePickerAsync(options);
            }
        
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while picking files");
            return null;
        }
    }
    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.TryGetFiles() is not null ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files is not null)
        {
            _ = ProcessDroppedOrPastedFilesAsync(files);
        }
    }
    private async void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        try
        {
            var keyform = KeyModifiers.Control;
            if (OperatingSystem.IsMacOS()) keyform = KeyModifiers.Meta;

            if (e.Key == Key.V && e.KeyModifiers.HasFlag(keyform))
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard == null) return;

                var files = await clipboard.TryGetFilesAsync();
                if (files != null && files.Length != 0)
                {
                    e.Handled = true; 
                    await ProcessDroppedOrPastedFilesAsync(files);
                }

                var bitmap = await clipboard.TryGetBitmapAsync();
                if (bitmap != null)
                {
                    e.Handled = true;
                    await ProcessPastedBitmapAsync(bitmap);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while processing pasted files");
        }
    }
    private async Task ProcessDroppedOrPastedFilesAsync(IEnumerable<IStorageItem> items)
    {
        var storageFiles = items.OfType<IStorageFile>().ToList();
        if (storageFiles.Count == 0) return;
        
        if (DataContext is MainChatViewModel vm)
        {
            await vm.StageFilesAsync(storageFiles);
        }
    }
    private Task ProcessPastedBitmapAsync(Bitmap bitmap)
    {
        try
        {
            if (DataContext is not MainChatViewModel vm) return Task.CompletedTask;
            using var ms = new MemoryStream();
            bitmap.Save(ms);
            var bytes = ms.ToArray();
            
            var attachment = new StagedAttachment
            {
                FileName = $"Pasted_Image_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                Data = bytes,
                MimeType = "image/png",
                PreviewBitmap = bitmap
            };
            vm.StagedAttachments.Add(attachment);

            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            return Task.FromException(exception);
        }
    }
}