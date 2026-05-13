namespace ChatSystem.Infrastructure.Messaging;


public sealed record MessageSentEvent(
    Guid MessageId,
    Guid ConversationId,
    Guid SenderId,
    string SenderName,
    string Body,
    string Status,
    DateTime SentAt);


public sealed record MessageStatusUpdatedEvent(
    Guid MessageId,
    string NewStatus,
    DateTime UpdatedAt);