using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AsyncImageLoader;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Messaging;
using Ermine.Models;
using Ermine.ViewModels;
using Serilog;

namespace Ermine.Views;

public partial class MainChatView : UserControl
{
    private MainChatViewModel? _vm;
    private ObservableCollection<Message>? _messages;
    private ScrollViewer? _scrollViewer;
    private CancellationTokenSource? _scrollCts;
    private bool _stickToBottom = true;
    private bool _prependingMessages;
    private double _savedScrollOffset;
    private double _savedExtent;
    public MainChatView()
    {
        InitializeComponent();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        this.FindControl<TextBox>("InputTextBox")
            ?.AddHandler(KeyDownEvent, OnTextBoxKeyDown, RoutingStrategies.Tunnel);

        WeakReferenceMessenger.Default.Register<PickFilesMessage>(this,
            (_, msg) => msg.Reply(PickFilesAsync(msg.Options)));

        WeakReferenceMessenger.Default.Register<MainChatViewModel.ScrollToMessageRequest>(this, (_, msg) =>
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                MessageList.ScrollIntoView(msg.Target);
                _stickToBottom = false;
            }));

        MessageList.ContainerClearing += (_, e) =>
        {
            foreach (var img in e.Container.GetVisualDescendants().OfType<Image>())
            {
                ImageLoader.SetSource(img, null);
                img.Source = null;
            }
        };

        MessageList.TemplateApplied += (_, _) =>
        {
            BindScrollViewer();
            _stickToBottom = true;
            ScheduleScrollToBottom(force: true);
        };
        
        WeakReferenceMessenger.Default.Register<MainChatViewModel.PrependingMessagesNotification>(this, (_, _) =>
        {
            _savedScrollOffset = _scrollViewer?.Offset.Y ?? 0;
            _savedExtent = _scrollViewer?.Extent.Height ?? 0;
            _prependingMessages = true;
        });
        
        WeakReferenceMessenger.Default.Register<MainChatViewModel.PrependedMessagesNotification>(this, (_, _) =>
        {
            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (_scrollViewer == null) return;

                double newExtent = _scrollViewer.Extent.Height;
                int passes = 0;

                Action noOp = () => { };

                while (newExtent <= _savedExtent && passes < 10)
                {
                    await Dispatcher.UIThread.InvokeAsync(noOp, DispatcherPriority.Render);
                    newExtent = _scrollViewer.Extent.Height;
                    passes++;
                }

                var extentGrowth = newExtent - _savedExtent;
        
                if (extentGrowth > 0)
                {
                    _scrollViewer.SetCurrentValue(ScrollViewer.OffsetProperty,
                        _scrollViewer.Offset.WithY(_savedScrollOffset + extentGrowth));
                
                    await Dispatcher.UIThread.InvokeAsync(noOp, DispatcherPriority.Render);
                    var finalExtent = _scrollViewer.Extent.Height;
                    if (Math.Abs(finalExtent - newExtent) > 1) 
                    {
                        var correction = finalExtent - newExtent;
                        _scrollViewer.SetCurrentValue(ScrollViewer.OffsetProperty,
                            _scrollViewer.Offset.WithY(_scrollViewer.Offset.Y + correction));
                    }
                }
            });
        });
    }


    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        BindViewModel();
        BindScrollViewer();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        UnbindScrollViewer();
        UnbindViewModel();
        base.OnUnloaded(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        BindViewModel();
    }


    private void BindViewModel()
    {
        var vm = DataContext as MainChatViewModel;
        if (ReferenceEquals(_vm, vm)) return;

        UnbindViewModel();
        _vm = vm;

        if (_vm == null) return;
        _vm.PropertyChanged += OnVmPropertyChanged;
        BindMessages(_vm.CurrentMessages, scrollToBottom: true);
    }

    private void UnbindViewModel()
    {
        if (_vm == null) return;
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = null;
        UnbindMessages();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainChatViewModel.CurrentMessages))
        {
            _stickToBottom = true;
            BindMessages(_vm?.CurrentMessages, scrollToBottom: true);
        }
        else if (e.PropertyName == nameof(MainChatViewModel.SelectedChannel))
        {
            _stickToBottom = true;
        }
    }

    private void BindMessages(ObservableCollection<Message>? messages, bool scrollToBottom)
    {
        if (ReferenceEquals(_messages, messages)) return;

        UnbindMessages();
        _messages = messages;
        if (_messages != null)
            _messages.CollectionChanged += OnMessagesChanged;

        if (scrollToBottom && _scrollViewer != null)
        {
            _stickToBottom = true;
            ScheduleScrollToBottom(force: true);
        }
    }

    private void UnbindMessages()
    {
        if (_messages == null) return;
        _messages.CollectionChanged -= OnMessagesChanged;
        _messages = null;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;

        if (e.NewStartingIndex == 0 && _messages?.Count > e.NewItems?.Count)
        {
            _prependingMessages = true;
            return;
        }

        if (_stickToBottom)
            ScheduleScrollToBottom(force: false);
    }


    private void BindScrollViewer()
    {
        var sv = MessageList.FindDescendantOfType<ScrollViewer>();
        if (ReferenceEquals(_scrollViewer, sv)) return;

        UnbindScrollViewer();
        _scrollViewer = sv;
        if (_scrollViewer != null)
            _scrollViewer.ScrollChanged += OnScrollChanged;
    }

    private void UnbindScrollViewer()
    {
        if (_scrollViewer == null) return;
        _scrollViewer.ScrollChanged -= OnScrollChanged;
        _scrollViewer = null;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scrollViewer == null) return;

        if (_prependingMessages)
        {
            _prependingMessages = false;
            return;
        }

        var distanceFromBottom = _scrollViewer.Extent.Height
                                 - _scrollViewer.Viewport.Height
                                 - _scrollViewer.Offset.Y;

        if (distanceFromBottom <= 20)
            _stickToBottom = true;
        else if (e.OffsetDelta.Y < 0)
            _stickToBottom = false;

        if (!_stickToBottom && _scrollViewer.Offset.Y < 300 && _vm?.SelectedChannel != null)
            _ = _vm.FetchOlderMessagesAsync(_vm.SelectedChannel.Id);
    }

    private void ScheduleScrollToBottom(bool force)
    {
        var sv = _scrollViewer ?? MessageList.FindDescendantOfType<ScrollViewer>();
        if (sv == null)
        {
            return;
        }

        if (_scrollCts != null && !_scrollCts.IsCancellationRequested)
        {
            return;
        }

        _scrollCts?.Dispose();
        _scrollCts = new CancellationTokenSource();
        var cts = _scrollCts;

        _ = DoScrollToBottomAsync(sv, cts.Token).ContinueWith(_ =>
        {
            if (ReferenceEquals(_scrollCts, cts))
                _scrollCts = null;
        }, TaskScheduler.Default);
    }
private static async Task DoScrollToBottomAsync(ScrollViewer sv, CancellationToken token)
{
    try
    {
        double lastExtent = -1;
        int stableCount = 0;

        for (int i = 0; i < 20; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            if (token.IsCancellationRequested) return;

            var extent = sv.Extent.Height;
            var viewport = sv.Viewport.Height;

            if (extent == 0)
            {
                lastExtent = extent;
                stableCount = 0;
                continue;
            }

            if (Math.Abs(extent - lastExtent) < 0.1)
                stableCount++;
            else
                stableCount = 0;

            lastExtent = extent;

            if (stableCount < 2) continue;

            if (extent <= viewport) return;

            var distanceFromBottom = extent - viewport - sv.Offset.Y;
            if (distanceFromBottom <= 50) return;

            sv.SetCurrentValue(ScrollViewer.OffsetProperty, sv.Offset.WithY(double.MaxValue));

            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            if (token.IsCancellationRequested) return;

            distanceFromBottom = sv.Extent.Height - sv.Viewport.Height - sv.Offset.Y;

            if (distanceFromBottom <= 50) return;
            
            lastExtent = -1;
            stableCount = 0;
        }

        Log.Warning("DoScrollToBottomAsync: gave up after 20 passes");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error scrolling message list to bottom");
    }
}
    private async Task<IReadOnlyList<IStorageFile>?> PickFilesAsync(FilePickerOpenOptions options)
    {
        try
        {
            var window = TopLevel.GetTopLevel(this);
            if (window?.StorageProvider is { } sp) return await sp.OpenFilePickerAsync(options);

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
        if (files is not null) _ = ProcessDroppedOrPastedFilesAsync(files);
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

        if (DataContext is MainChatViewModel vm) await vm.StageFilesAsync(storageFiles);
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