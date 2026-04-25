using System.Xml.Schema;

namespace ChatSystem.Application.DTOs;


public sealed class SendMessageDto
{
    public Guid ConversationId { get; init; }
    public Guid SenderId { get; init; }
    public string Body { get; init; } = string.Empty;
}


public sealed class CreateGroupConversationDto
{
    public Guid CreatedByUserId { get; init; }
    public string Title { get; init; } = string.Empty;

    public List<Guid> ParticipantIds { get; init; } = new();
}


public sealed class CreateDirectConversationDto
{
    public Guid InitiatorUserId { get; init; }
    public Guid RecipientUserId { get; init; }
}

// Carries the data needed to add a participant to an existing group.
public sealed class AddParticipantDto
{
    public Guid ConversationId { get; init; }

    /// <summary>The admin user performing the add action.</summary>
    public Guid RequestedByUserId { get; init; }

    public Guid UserIdToAdd { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// OUTBOUND DTOs  (response shapes returned FROM the Application layer)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The shape returned to callers after a message is sent or retrieved.
/// Domain entities are never returned directly — DTOs decouple the API
/// contract from internal domain structure and prevent over-posting.
/// </summary>
public sealed class MessageResponseDto
{
    public Guid Id { get; init; }
    public Guid ConversationId { get; init; }
    public Guid SenderId { get; init; }
    public string SenderName { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime SentAt { get; init; }
    public DateTime? DeliveredAt { get; init; }
    public DateTime? ReadAt { get; init; }
    public bool IsDeleted { get; init; }
}

public sealed class ConversationDto
{
    public Guid Id { get; init; }

    /// <summary>
    /// Null for Direct conversations. The UI derives the display name
    /// from ParticipantName in that case.
    /// </summary>
    public string? Title { get; init; }

    public string Type { get; init; } = string.Empty;
    public DateTime? LastMessageAt { get; init; }

    // Count of messages in this conversation sent after the caller's LastReadAt.
    // Computed in the query — not stored on the entity.
    public int UnreadCount { get; init; }

    // Preview of the last message body (truncated to 100 chars by the query).
    public string? LastMessagePreview { get; init; }
}

// Lightweight participant shape used inside ConversationDetailDto.
public sealed class ParticipantDto
{
    public Guid UserId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? AvatarUrl { get; init; }
    public bool IsAdmin { get; init; }
    public bool IsOnline { get; init; }
}

// Full conversation detail including participants.
// Returned when a user opens a specific conversation.
public sealed class ConversationDetailDto
{
    public Guid Id { get; init; }
    public string? Title { get; init; }
    public string Type { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public List<ParticipantDto> Participants { get; init; } = new();
    public List<MessageResponseDto> Messages { get; init; } = new();
}