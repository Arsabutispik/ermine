using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Ermine.Models;
using AsyncImageLoader;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Ermine.Core;

namespace Ermine.Behaviors;

public class MessageContentBehavior
{
    public static readonly AttachedProperty<Message?> FormattedMessageProperty =
        AvaloniaProperty.RegisterAttached<MessageContentBehavior, SelectableTextBlock, Message?>("FormattedMessage");

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> FailedEmojis = new();

    public static Message? GetFormattedMessage(SelectableTextBlock element) =>
        element.GetValue(FormattedMessageProperty);

    public static void SetFormattedMessage(SelectableTextBlock element, Message? value) =>
        element.SetValue(FormattedMessageProperty, value);

    public static readonly AttachedProperty<bool> IsPreviewModeProperty =
        AvaloniaProperty.RegisterAttached<Control, SelectableTextBlock, bool>("IsPreviewMode", defaultValue: false);

    public static void SetIsPreviewMode(SelectableTextBlock element, bool value)
        => element.SetValue(IsPreviewModeProperty, value);

    public static bool GetIsPreviewMode(SelectableTextBlock element)
        => element.GetValue(IsPreviewModeProperty);

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

        if (GetIsPreviewMode(tb))
        {
            var normalizedContent = content.Replace("\r\n", "\n");
            var firstNewlineIndex = normalizedContent.IndexOf('\n');

            if (firstNewlineIndex >= 0)
            {
                content = normalizedContent.Substring(0, firstNewlineIndex).TrimEnd() + "…";
            }
        }

        var tokens = Regex.Split(content, "(<@[a-zA-Z0-9]+>|\\:[a-zA-Z0-9]+\\:)").Where(t => !string.IsNullOrEmpty(t))
            .ToList();

        bool isJumbo = tokens.Count == 1 && tokens[0].StartsWith(":") && tokens[0].EndsWith(":");

        double emojiSize = isJumbo ? 64 : 32;
        var margin = isJumbo ? new Thickness(0, 4) : new Thickness(2, 0, 2, 0);

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
                    RenderOptions.SetBitmapInterpolationMode(avatarImage, BitmapInterpolationMode.HighQuality);

                    if (isAvatarAvailable)
                    {
                        avatarImage[ImageLoader.SourceProperty] = avatarUrl;
                    }

                    var border = new Border
                    {
                        Background = backgroundBrush,
                        CornerRadius = new CornerRadius(4),
                        Padding = isAvatarAvailable ? new Thickness(2, 1, 4, 1) : new Thickness(4, 1, 4, 1),
                        Margin = new Thickness(2, 0, 0, 0),
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
                        BaselineAlignment = BaselineAlignment.Center
                    });
                    continue;
                }
            }
            else if (token.StartsWith(":") && token.EndsWith(":"))
            {
                var emojiId = token.Substring(1, token.Length - 2);

                if (emojiId.Length != 26)
                {
                    tb.Inlines.Add(new Run { Text = token });
                    continue;
                }

                if (FailedEmojis.ContainsKey(emojiId))
                {
                    tb.Inlines.Add(new Run { Text = token });
                    continue;
                }

                string emojiUrl = $"{ApiClient.AutumnUrl}/emojis/{emojiId}";

                var isAnimated = GlobalCache.Emojis.TryGetValue(emojiId, out var emoji) && emoji.Animated;
                var emojiControl = CreateEmojiControl(emojiUrl, emojiSize, margin, isAnimated);


                ToolTip.SetTip(emojiControl, $":{emojiId}:");
                ToolTip.SetPlacement(emojiControl, PlacementMode.Top);
                ToolTip.SetVerticalOffset(emojiControl, -5);

                var inlineContainer = new InlineUIContainer
                {
                    BaselineAlignment = BaselineAlignment.Center
                };

                if (emoji != null)
                {
                    SetEmojiTooltip(emojiControl, emoji);
                    inlineContainer.Child = emojiControl;
                }
                else
                {
                    inlineContainer.Child = emojiControl;
                    Task.Run(async () =>
                    {
                        var fetchedEmoji = await ApiClient.GetEmoji(emojiId);

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (fetchedEmoji != null && !string.IsNullOrEmpty(fetchedEmoji.Name))
                            {
                                GlobalCache.Emojis[emojiId] = fetchedEmoji;

                                if (fetchedEmoji.Animated)
                                {
                                    var animatedEmojiControl = CreateEmojiControl(emojiUrl, emojiSize, margin, true);
                                    SetEmojiTooltip(animatedEmojiControl, fetchedEmoji);
                                    inlineContainer.Child = animatedEmojiControl;
                                }
                                else
                                {
                                    SetEmojiTooltip(emojiControl, fetchedEmoji);
                                }
                            }
                            else
                            {
                                FailedEmojis.TryAdd(emojiId, true);
                                inlineContainer.Child = new TextBlock
                                {
                                    Text = token,
                                    VerticalAlignment = VerticalAlignment.Center
                                };
                            }
                        });
                    });
                }

                tb.Inlines.Add(inlineContainer);
                continue;
            }

            tb.Inlines.Add(new Run { Text = token });
        }
    }

    private static Control CreateEmojiControl(string emojiUrl, double emojiSize, Thickness margin, bool animated)
    {
        if (animated)
        {
            var img = new Image
            {
                Width = emojiSize,
                Height = emojiSize,
                Stretch = Stretch.Uniform,
                Margin = margin,
            };

            var framesTask = Task.Run(() => App.ImageCache.ProvideFramesAsync(emojiUrl));

            framesTask.ContinueWith(t =>
            {
                var frames = t.Result;
                if (frames == null || frames.Count == 0) return;
        
                img.Source = frames[0].Bitmap;
        
                if (frames.Count > 1)
                {
                    int index = 0;
                    void Tick()
                    {
                        index = (index + 1) % frames.Count;
                        img.Source = frames[index].Bitmap;
                        Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            await Task.Delay(frames[index].DelayMs);
                            Tick();
                        });
                    }
                    Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await Task.Delay(frames[0].DelayMs);
                        Tick();
                    });
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());

            return img;
        }

        var staticImg = new Image
        {
            Width = emojiSize,
            Height = emojiSize,
            Stretch = Stretch.Uniform,
            Margin = margin,
        };
        RenderOptions.SetBitmapInterpolationMode(staticImg, BitmapInterpolationMode.HighQuality);
        staticImg[ImageLoader.SourceProperty] = emojiUrl;
        return staticImg;
    }

    private static void SetEmojiTooltip(Control emojiControl, Emoji emoji)
    {
        var tooltipContent = new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            MinWidth = 100
        };

        tooltipContent.Children.Add(new TextBlock
        {
            Text = $":{emoji.Name}:",
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        if (emoji.Parent.Type == EmojiParentType.Server)
        {
            if (GlobalCache.Servers.TryGetValue(emoji.Parent.Id, out var server))
            {
                var serverLine = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                if (!string.IsNullOrEmpty(server.IconUrl))
                {
                    var icon = new Image
                    {
                        Width = 16,
                        Height = 16,
                        [ImageLoader.SourceProperty] = server.IconUrl
                    };

                    RenderOptions.SetBitmapInterpolationMode(icon, BitmapInterpolationMode.HighQuality);
                    serverLine.Children.Add(new Border
                    {
                        CornerRadius = new CornerRadius(8),
                        ClipToBounds = true,
                        Child = icon
                    });
                }

                serverLine.Children.Add(new TextBlock
                {
                    Text = $"From {server.Name}",
                    FontSize = 11,
                    Opacity = 0.8,
                    VerticalAlignment = VerticalAlignment.Center
                });
                tooltipContent.Children.Add(serverLine);
            }
            else
            {
                tooltipContent.Children.Add(new TextBlock
                {
                    Text = "Private Server",
                    FontSize = 11,
                    Opacity = 0.6,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }
        }

        ToolTip.SetTip(emojiControl, tooltipContent);
    }
}