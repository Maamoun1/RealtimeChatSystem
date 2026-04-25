using ChatSystem.Domain.Entities;

namespace ChatSystem.Application.Interfaces;

public interface IConversationRepository
{
    Task<Conversation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    // Returns all conversations a user participates in, ordered by
    Task<IReadOnlyList<Conversation>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task AddAsync(Conversation conversation, CancellationToken cancellationToken = default);

    Task UpdateAsync(Conversation conversation, CancellationToken cancellationToken = default);


    Task AddParticipantAsync(
        ConversationParticipant participant,
        CancellationToken cancellationToken = default);

    Task RemoveParticipantAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<ConversationParticipant?> GetParticipantAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConversationParticipant>> GetParticipantsAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);

    // Used as an authorisation guard: "is this user allowed to read/write here?"
    Task<bool> IsParticipantAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// Persists LastReadAt updates made via ConversationParticipant.MarkAsRead().
    Task UpdateParticipantAsync(
        ConversationParticipant participant,
        CancellationToken cancellationToken = default);
}