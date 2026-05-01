using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
    }
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (DataContext is MainChatViewModel vm)
        {
            vm.CurrentMessages.CollectionChanged += OnMessagesChanged;
        }
    }
    private CancellationTokenSource? _scrollCts;

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;

        var scrollViewer = MessageList.FindDescendantOfType<ScrollViewer>();
        if (scrollViewer == null) return;

        var isAtBottom = scrollViewer.Offset.Y >= (scrollViewer.Extent.Height - scrollViewer.Viewport.Height - 50);
        if (!isAtBottom && scrollViewer.Extent.Height != 0) return;

        _scrollCts?.Cancel();
        _scrollCts = new CancellationTokenSource();
        var token = _scrollCts.Token;

        Dispatcher.UIThread.Post(() =>
        {
            if (!token.IsCancellationRequested)
                scrollViewer.ScrollToEnd();
        }, DispatcherPriority.Loaded);
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