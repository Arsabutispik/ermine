using System.Collections.Generic;

namespace Ermine.Models;

public class AppSettings
{
    public string? SessionToken { get; set; }
    public string? LastInstanceUrl { get; set; }
    public List<string> SavedInstanceUrls { get; set; } = new();
    public string? LastServerId { get; set; }
    public string? LastChannelId { get; set; }
    
    public bool MentionOnReply { get; set; } = false; 
}