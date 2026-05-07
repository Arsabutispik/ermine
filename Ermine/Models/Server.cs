using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Ermine.Models;


public record Server(
    [property: JsonPropertyName("_id")] string Id,
    [property: JsonPropertyName("channels")] IReadOnlyList<string> Channels,
    [property: JsonPropertyName("default_permissions")] long DefaultPermissions,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("owner")] string Owner,
    [property: JsonPropertyName("analytics")] bool Analytics,
    [property: JsonPropertyName("banner")] Attachment? Banner,
    [property: JsonPropertyName("categories")] IReadOnlyList<Category>? Categories,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("discoverable")] bool Discoverable,
    [property: JsonPropertyName("flags")] uint Flags,
    [property: JsonPropertyName("icon")] Attachment? Icon,
    [property: JsonPropertyName("nsfw")] bool Nsfw
    // TODO: Add these fields
    // [property: JsonPropertyName("roles")] string[] Roles,
    // [property: JsonPropertyName("system_messages")] bool SystemMessages,
) : INotifyPropertyChanged
{
    [JsonIgnore]
    public bool HasUnreads
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasUnreads)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    
    public string IconUrl => Icon != null
        ? $"{ApiClient.AutumnUrl}/icons/{Icon.Id}"
        : $"https://api.dicebear.com/7.x/initials/png?seed={Uri.EscapeDataString(Name)}";
    
    public string? BannerUrl => Banner != null 
        ? $"{ApiClient.AutumnUrl}/banners/{Banner.Id}" 
        : null;   
}