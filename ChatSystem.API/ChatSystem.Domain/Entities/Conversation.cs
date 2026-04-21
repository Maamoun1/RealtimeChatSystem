using ChatSystem.Domain.Enums;
using ChatSystem.Domain.Exceptions;

namespace ChatSystem.Domain.Entities;


public class Conversation
{


    public Guid Id { get; private set; }
    public ConversationType Type { get; private set; }

    // Null for Direct conversations. Required for Group conversations.
    public string? Title { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    // Null until the first message is sent.
    public DateTime? LastMessageAt { get; private set; }

    // -------------------------------------------------------------------------
    // Factory methods — preferred over direct constructors for named intent
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a 1-to-1 Direct conversation.
    /// Title is explicitly null — no caller can accidentally set one.
    /// </summary>
    public static Conversation CreateDirect(Guid createdByUserId)
    {
        if (createdByUserId == Guid.Empty)
            throw new DomainException("CreatedByUserId cannot be empty.");

        return new Conversation
        {
            Id = Guid.NewGuid(),
            Type = ConversationType.Direct,
            Title = null,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTime.UtcNow,
            LastMessageAt = null
        };
    }

    public static Conversation CreateGroup(Guid createdByUserId, string title)
    {
        if (createdByUserId == Guid.Empty)
            throw new DomainException("CreatedByUserId cannot be empty.");

        title = title?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(title))
            throw new DomainException("Group conversation title cannot be empty.");

        if (title.Length > 200)
            throw new DomainException("Group conversation title cannot exceed 200 characters.");

        return new Conversation
        {
            Id = Guid.NewGuid(),
            Type = ConversationType.Group,
            Title = title,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTime.UtcNow,
            LastMessageAt = null
        };
    }

    private Conversation() { }


    public void UpdateLastMessageAt(DateTime sentAt)
    {
        // Guard: never allow the timestamp to move backwards.
        // Out-of-order updates can happen during retry scenarios.
        if (LastMessageAt.HasValue && sentAt <= LastMessageAt.Value)
            return;

        LastMessageAt = sentAt;
    }

    public void UpdateTitle(string newTitle)
    {
        if (Type == ConversationType.Direct)
            throw new DomainException("Cannot set a title on a direct conversation.");

        newTitle = newTitle?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(newTitle))
            throw new DomainException("Group conversation title cannot be empty.");

        if (newTitle.Length > 200)
            throw new DomainException("Group conversation title cannot exceed 200 characters.");

        Title = newTitle;
    }
}