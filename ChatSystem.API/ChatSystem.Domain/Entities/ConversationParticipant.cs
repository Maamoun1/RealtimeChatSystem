using ChatSystem.Domain.Exceptions;

namespace ChatSystem.Domain.Entities;


public class ConversationParticipant
{
    
    public Guid Id { get; private set; }
    public Guid ConversationId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime JoinedAt { get; private set; }
    public DateTime? LastReadAt { get; private set; }

    public bool IsAdmin { get; private set; }

    public static ConversationParticipant Create(
        Guid conversationId,
        Guid userId,
        bool isAdmin = false)
    {
        if (conversationId == Guid.Empty)
            throw new DomainException("ConversationId cannot be empty.");

        if (userId == Guid.Empty)
            throw new DomainException("UserId cannot be empty.");

        return new ConversationParticipant
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            UserId = userId,
            JoinedAt = DateTime.UtcNow,
            LastReadAt = null,
            IsAdmin = isAdmin
        };
    }

    private ConversationParticipant() { }

    public void MarkAsRead(DateTime readAt)
    {
        if (LastReadAt.HasValue && readAt <= LastReadAt.Value)
            return;

        LastReadAt = readAt;
    }

    public void PromoteToAdmin()
    {
        if (IsAdmin)
            throw new DomainException("Participant is already an admin.");

        IsAdmin = true;
    }

    public void DemoteFromAdmin()
    {
        if (!IsAdmin)
            throw new DomainException("Participant is not an admin.");

        IsAdmin = false;
    }
}