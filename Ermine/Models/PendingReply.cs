using CommunityToolkit.Mvvm.ComponentModel;
using Ermine.Models;

namespace Ermine.Models;

public partial class PendingReply : ObservableObject
{
    public Message TargetMessage { get; }
    
    [ObservableProperty]
    public partial bool Mention { get; set; }

    public bool IsSelfReply { get; }

    public PendingReply(Message targetMessage, bool initialMention, string currentUserId)
    {
        TargetMessage = targetMessage;
        Mention = initialMention;
        IsSelfReply = targetMessage.Author == currentUserId;
    }
}