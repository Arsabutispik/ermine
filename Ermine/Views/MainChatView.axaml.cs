using System.Collections.Specialized;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ermine.ViewModels;

namespace Ermine.Views;

public partial class MainChatView : UserControl
{
    public MainChatView()
    {
        InitializeComponent();
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
}