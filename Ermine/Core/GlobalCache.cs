using System.Collections.Concurrent;
using Ermine.Models;

namespace Ermine.Core;

public class GlobalCache
{
    public static ConcurrentDictionary<string, Emoji> Emojis { get; } = new();

    public static ConcurrentDictionary<string, Server> Servers { get; } = new();
    
    public static ConcurrentDictionary<string, Channel> Channels { get; } = new();

    public static ConcurrentDictionary<string, User> Users { get; } = new();
    
    public static string? CurrentUserId { get; set; }
}