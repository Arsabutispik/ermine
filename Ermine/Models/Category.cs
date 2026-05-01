using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Ermine.Models;

public record Category(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("channels")] IReadOnlyList<string> Channels
);

public partial class ChannelGroup : ObservableObject
{
    // If null or empty, this represents the "uncategorized" channels at the top
    public string? CategoryName { get; set; }
    
    // Helper property to hide the header for uncategorized channels
    public bool IsCategory => !string.IsNullOrEmpty(CategoryName);
    
    [ObservableProperty]
    private bool _isExpanded = true;
    
    public ObservableCollection<Channel> Channels { get; set; } = new();
}