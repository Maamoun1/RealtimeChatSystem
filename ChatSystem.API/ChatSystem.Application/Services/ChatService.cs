using ChatSystem.Application.DTOs;
using ChatSystem.Application.Interfaces;
using ChatSystem.Application.Validators;
using ChatSystem.Domain.Entities;
using ChatSystem.Domain.Exceptions;

namespace ChatSystem.Application.Services;

public sealed class ChatService
{
    private readonly IMessageRepository _messageRepository;
    private readonly IConversationRepository _conversationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IMessageQueueService _queueService;
    private readonly SendMessageValidator _validator;

    public ChatService(
    IMessageRepository messageRepository,
    IConversationRepository conversationRepository,
    IUserRepository userRepository,
    IMessageQueueService queueService,
    SendMessageValidator validator)
    {
        _messageRepository = messageRepository;
        _conversationRepository = conversationRepository;
        _userRepository = userRepository;
        _queueService = queueService;
        _validator = validator;
    }

    public async Task<MessageResponseDto> SendMessageAsync(
        SendMessageDto dto,
        CancellationToken cancellationToken = default)
    {
        // ── Step 1: Validate input ────────────────────────────────────────────
        var validation = _validator.Validate(dto);
        if (!validation.IsValid)
            throw new DomainException(
                $"Invalid message payload: {validation.ErrorSummary()}");

        // ── Step 2: Guard — sender exists ─────────────────────────────────────
        var sender = await _userRepository.GetByIdAsync(dto.SenderId, cancellationToken)
            ?? throw new DomainException($"Sender {dto.SenderId} not found.");

        // ── Step 3: Guard — conversation exists and sender is a participant ───
        var conversation = await _conversationRepository
            .GetByIdAsync(dto.ConversationId, cancellationToken)
            ?? throw new DomainException($"Conversation {dto.ConversationId} not found.");

        var isParticipant = await _conversationRepository
            .IsParticipantAsync(dto.ConversationId, dto.SenderId, cancellationToken);

        if (!isParticipant)
            throw new DomainException(
                "Sender is not a participant of this conversation.");

        // ── Step 4: Domain creates the message ────────────────────────────────
        // Business rules (empty body, max length) are enforced inside Message.Create().
        // We do not duplicate those checks here — the Domain owns them.
        var message = Message.Create(dto.ConversationId, dto.SenderId, dto.Body);

        // ── Step 5: Persist ───────────────────────────────────────────────────
        await _messageRepository.AddAsync(message, cancellationToken);

        // ── Step 6: Update conversation's last-activity timestamp ─────────────
        // Domain guard inside UpdateLastMessageAt prevents backwards movement.
        conversation.UpdateLastMessageAt(message.SentAt);
        await _conversationRepository.UpdateAsync(conversation, cancellationToken);

        // ── Step 7: Publish event to message queue ────────────────────────────
        // Consumers (SignalR dispatcher, push-notification service) subscribe
        // independently. Failure here does NOT roll back the message — the
        // retry background service will re-publish undelivered messages.
        var responseDto = MapToResponseDto(message, sender.DisplayName);

        await _queueService.PublishMessageAsync(responseDto, cancellationToken);

        return responseDto;
    }

    /// <summary>
    /// Returns a page of messages for a conversation using cursor-based pagination.
    /// The caller must be a participant — this is an authorisation check, not a
    /// domain rule, so it lives here in the Application layer.
    /// </summary>
    public async Task<IReadOnlyList<MessageResponseDto>> GetMessagesAsync(
        Guid conversationId,
        Guid requestingUserId,
        int pageSize = 50,
        DateTime? cursorSentAt = null,
        CancellationToken cancellationToken = default)
    {
        // Guard: only participants may read a conversation's history.
        var isParticipant = await _conversationRepository
            .IsParticipantAsync(conversationId, requestingUserId, cancellationToken);

        if (!isParticipant)
            throw new DomainException("User is not a participant of this conversation.");

        var messages = await _messageRepository.GetByConversationIdAsync(
            conversationId, pageSize, cursorSentAt, cancellationToken);

        // Collect unique sender IDs to avoid N+1 user lookups.
        // In a production system this would be a single IN-query via a ReadModel.
        // Here we resolve them individually — acceptable for a portfolio project.
        var senderIds = messages.Select(m => m.SenderId).Distinct();
        var senderNames = new Dictionary<Guid, string>();

        foreach (var senderId in senderIds)
        {
            var user = await _userRepository.GetByIdAsync(senderId, cancellationToken);
            senderNames[senderId] = user?.DisplayName ?? "Unknown";
        }

        return messages
            .Select(m => MapToResponseDto(m, senderNames[m.SenderId]))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Called by the SignalR Hub when a user reconnects.
    /// Finds all messages that were sent while the user was offline and
    /// transitions them from Sent → Delivered using the domain method.
    /// </summary>
    public async Task DeliverPendingMessagesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var pending = await _messageRepository
            .GetUndeliveredMessagesForUserAsync(userId, cancellationToken);

        foreach (var message in pending)
        {
            // Domain enforces the Sent → Delivered transition and sets DeliveredAt.
            message.MarkAsDelivered();
            await _messageRepository.UpdateAsync(message, cancellationToken);

            // Notify the sender that their message was delivered.
            await _queueService.PublishStatusUpdateAsync(
                message.Id,
                "Delivered",
                cancellationToken);
        }
    }

    /// <summary>
    /// Called when a participant opens a conversation.
    /// Advances unread messages to Read and updates the participant's LastReadAt.
    /// </summary>
    public async Task MarkConversationAsReadAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var participant = await _conversationRepository
            .GetParticipantAsync(conversationId, userId, cancellationToken)
            ?? throw new DomainException("User is not a participant of this conversation.");

        // Update the participant's read cursor.
        // Domain's monotonic guard ensures this never moves backwards.
        participant.MarkAsRead(DateTime.UtcNow);
        await _conversationRepository.UpdateParticipantAsync(participant, cancellationToken);

        // Retrieve messages that are still in Sent or Delivered state for this user.
        var unread = await _messageRepository.GetByConversationIdAsync(
            conversationId, 200, null, cancellationToken);

        foreach (var message in unread.Where(m =>
            m.SenderId != userId &&
            m.Status != Domain.Enums.MessageStatus.Read &&
            !m.IsDeleted))
        {
            // Domain method sets ReadAt and advances Status.
            message.MarkAsRead();
            await _messageRepository.UpdateAsync(message, cancellationToken);

            await _queueService.PublishStatusUpdateAsync(
                message.Id, "Read", cancellationToken);
        }
    }

    /// <summary>
    /// Soft-deletes a message. Authorization (only sender can delete) is enforced
    /// here in the Application layer — it is a use-case rule, not a domain rule.
    /// The actual deletion (Body scrub + IsDeleted flag) is handled by Message.Delete().
    /// </summary>
    public async Task DeleteMessageAsync(
        Guid messageId,
        Guid requestingUserId,
        CancellationToken cancellationToken = default)
    {
        var message = await _messageRepository.GetByIdAsync(messageId, cancellationToken)
            ?? throw new DomainException($"Message {messageId} not found.");

        // Authorization guard — only the sender may delete their own messages.
        if (message.SenderId != requestingUserId)
            throw new DomainException("Only the sender can delete this message.");

        // Domain method scrubs Body and sets IsDeleted = true.
        message.Delete();
        await _messageRepository.UpdateAsync(message, cancellationToken);
    }

    private static MessageResponseDto MapToResponseDto(Message message, string senderName)
        => new()
        {
            Id = message.Id,
            ConversationId = message.ConversationId,
            SenderId = message.SenderId,
            SenderName = senderName,
            Body = message.IsDeleted ? "[This message was deleted]" : message.Body,
            Status = message.Status.ToString(),
            SentAt = message.SentAt,
            DeliveredAt = message.DeliveredAt,
            ReadAt = message.ReadAt,
            IsDeleted = message.IsDeleted
        };
}
