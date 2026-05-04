using System;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Ermine.Models;
using AsyncImageLoader;

namespace Ermine.Behaviors;

public class MessageContentBehavior
{
    public static readonly AttachedProperty<Message?> FormattedMessageProperty =
        AvaloniaProperty.RegisterAttached<MessageContentBehavior, SelectableTextBlock, Message?>("FormattedMessage");

    public static Message? GetFormattedMessage(SelectableTextBlock element) => element.GetValue(FormattedMessageProperty);
    public static void SetFormattedMessage(SelectableTextBlock element, Message? value) => element.SetValue(FormattedMessageProperty, value);

    static MessageContentBehavior()
    {
        FormattedMessageProperty.Changed.AddClassHandler<SelectableTextBlock>(OnFormattedMessageChanged);
    }

    private static void OnFormattedMessageChanged(SelectableTextBlock tb, AvaloniaPropertyChangedEventArgs e)
    {
        tb.Inlines?.Clear();
        if (e.NewValue is not Message msg || string.IsNullOrEmpty(msg.Content))
            return;
            
        tb.Inlines ??= new InlineCollection();

        var content = msg.Content;
        var tokens = Regex.Split(content, "(<@[a-zA-Z0-9]+>)");

        foreach (var token in tokens)
        {
            if (string.IsNullOrEmpty(token)) continue;

            if (token.StartsWith("<@") && token.EndsWith(">"))
            {
                var id = token.Substring(2, token.Length - 3);
                
                if (msg.Mentions?.Contains(id) == true)
                {
                    var isCached = GlobalCache.Users.TryGetValue(id, out var user);
                    
                    var display = isCached && !string.IsNullOrWhiteSpace(user?.DisplayName) 
                        ? user.DisplayName 
                        : (isCached ? user?.Username : "Unknown User");
                        
                    var avatarUrl = isCached ? user?.AvatarUrl : null;
                    var isAvatarAvailable = !string.IsNullOrEmpty(avatarUrl);
                    var isCurrentUser = id == GlobalCache.CurrentUserId;

                    var backgroundBrush = new SolidColorBrush(Color.Parse(isCurrentUser ? "#5865f2" : "#3c4270"));
                    var foregroundBrush = new SolidColorBrush(Color.Parse(isCurrentUser ? "#ffffff" : "#c9cdfb"));

                    var avatarImage = new Image
                    {
                        Stretch = Stretch.UniformToFill
                    };
                    
                    if (isAvatarAvailable)
                    {
                        avatarImage[ImageLoader.SourceProperty] = avatarUrl;
                    }

                    var border = new Border
                    {
                        Background = backgroundBrush,
                        CornerRadius = new CornerRadius(4),
                        Padding = isAvatarAvailable ? new Thickness(2, 1, 4, 1) : new Thickness(4, 1, 4, 1),
                        Margin = new Thickness(2, 3, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center, 
                        Child = new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 4,
                            VerticalAlignment = VerticalAlignment.Center, 
                            Children =
                            {
                                new Border
                                {
                                    Width = 16, Height = 16,
                                    CornerRadius = new CornerRadius(8),
                                    ClipToBounds = true,
                                    IsVisible = isAvatarAvailable,
                                    Child = avatarImage
                                },
                                new TextBlock
                                {
                                    Text = $"@{display}",
                                    FontWeight = FontWeight.Medium,
                                    Foreground = foregroundBrush,
                                    VerticalAlignment = VerticalAlignment.Center
                                }
                            }
                        }
                    };

                    tb.Inlines.Add(new InlineUIContainer 
                    { 
                        Child = border,
                        BaselineAlignment = BaselineAlignment.TextBottom
                    });
                    continue;
                }
            }

            tb.Inlines.Add(new Run { Text = token });
        }
    }
}