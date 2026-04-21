using ChatSystem.Domain.Enums;
using ChatSystem.Domain.Exceptions;

namespace ChatSystem.Domain.Entities;


public class Message
{
  
    public Guid Id { get; private set; }
    public Guid ConversationId { get; private set; }
    public Guid SenderId { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public MessageStatus Status { get; private set; }

    public DateTime SentAt { get; private set; }

    public DateTime? DeliveredAt { get; private set; }

    public DateTime? ReadAt { get; private set; }


    public bool IsDeleted { get; private set; }

    public static Message Create(Guid conversationId, Guid senderId, string body)
    {
        if (conversationId == Guid.Empty)
            throw new DomainException("ConversationId cannot be empty.");

        if (senderId == Guid.Empty)
            throw new DomainException("SenderId cannot be empty.");

        body = body?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(body))
            throw new DomainException("Message body cannot be empty.");

        if (body.Length > 4000)
            throw new DomainException("Message body cannot exceed 4000 characters.");

        return new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            SenderId = senderId,
            Body = body,
            Status = MessageStatus.Sent,
            SentAt = DateTime.UtcNow,
            DeliveredAt = null,
            ReadAt = null,
            IsDeleted = false
        };
    }

    private Message() { }

    public void MarkAsDelivered()
    {
        if (IsDeleted)
            throw new DomainException("Cannot update status of a deleted message.");

        if (Status == MessageStatus.Delivered)
            return; // Idempotent — safe to call multiple times (retry scenarios)

        if (Status == MessageStatus.Read)
            throw new DomainException("Cannot mark a Read message as Delivered.");

        Status = MessageStatus.Delivered;
        DeliveredAt = DateTime.UtcNow;
    }

    public void MarkAsRead()
    {
        if (IsDeleted)
            throw new DomainException("Cannot update status of a deleted message.");

        if (Status == MessageStatus.Read)
            return; // Idempotent

        // Automatically fill DeliveredAt if it was skipped (offline → open scenario)
        if (Status == MessageStatus.Sent)
            DeliveredAt = DateTime.UtcNow;

        Status = MessageStatus.Read;
        ReadAt = DateTime.UtcNow;
    }

    public void Delete()
    {
        if (IsDeleted)
            throw new DomainException("Message is already deleted.");

        IsDeleted = true;
        Body = string.Empty; // Scrub the content — the body is no longer relevant
    }
}