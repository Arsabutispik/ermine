using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;

namespace Ermine.Models;

public record Interactions(
    [property: JsonPropertyName("reactions")]
    string[] Reactions,
    [property: JsonPropertyName("restrict_reactions")]
    bool RestrictReactions
);

public record Masquerade(
    [property: JsonPropertyName("avatar")] string? Avatar,
    [property: JsonPropertyName("colour")] string? Colour,
    [property: JsonPropertyName("name")] string? Name
    );

public record BulkMessageResponse(
    [property: JsonPropertyName("messages")] List<Message> Messages,
    [property: JsonPropertyName("users")] List<User> Users
    // [property: JsonPropertyName("members")] List<Member>? Members = null
);

public record Message(
    [property: JsonPropertyName("_id")] string Id,
    [property: JsonPropertyName("author")] string Author,
    [property: JsonPropertyName("channel")]
    string Channel,
    [property: JsonPropertyName("attachments")]
    IReadOnlyList<Attachment>? Attachments = null,
    [property: JsonPropertyName("content")]
    string? Content = null,
    [property: JsonPropertyName("edited")] DateTime? Edited = null,
    //[property: JsonPropertyName("embeds")] IReadOnlyList<Embed>? Embeds = null,
    [property: JsonPropertyName("flags")] uint? Flags = null,
    [property: JsonPropertyName("interactions")]
    Interactions? Interactions = null,
    [property: JsonPropertyName("masquerade")]
    Masquerade? Masquerade = null,
    //[property: JsonPropertyName("member")] Member? Member = null,
    [property: JsonPropertyName("mentions")]
    string[]? Mentions = null,
    [property: JsonPropertyName("nonce")] string? Nonce = null,
    [property: JsonPropertyName("pinned")] bool? Pinned = null,
    //[property: JsonPropertyName("reactions")] IReadOnlyList<Reaction>? Reactions = null,
    [property: JsonPropertyName("replies")]
    string[]? Replies = null,
    [property: JsonPropertyName("role_mentions")]
    string[]? RoleMentions = null,
    //[property: JsonPropertyName("system")] SystemMessage? System = null,
    [property: JsonPropertyName("user")] User? User = null
    //[property: JsonPropertyName("webhook")] MessageWebhook? = null
)
{
    [JsonIgnore]
    public bool IsMentionReply { get; init; }

    [JsonIgnore] public string DisplayAuthorName => (IsMentionReply ? "@" : "") + (Masquerade?.Name ?? User?.Username ?? Author);
    
    [JsonIgnore]
    public string? DisplayAvatarUrl => Masquerade?.Avatar ?? User?.AvatarUrl;


    [JsonIgnore]
    public bool MentionsCurrentUser =>
        !string.IsNullOrEmpty(GlobalCache.CurrentUserId) &&
        Mentions?.Contains(GlobalCache.CurrentUserId) == true;

    [JsonIgnore]
    public List<Message>? ResolvedReplies { get; set; }
    
}