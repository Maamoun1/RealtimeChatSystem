using ChatSystem.Domain.Entities;

namespace ChatSystem.Application.Interfaces;

public interface IMessageRepository
{
   
    Task AddAsync(Message message, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Message>> GetByConversationIdAsync(
        Guid conversationId,
        int pageSize,
        DateTime? cursorSentAt = null,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(Message message, CancellationToken cancellationToken = default);
    Task<Message?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Message>> GetUndeliveredMessagesForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}