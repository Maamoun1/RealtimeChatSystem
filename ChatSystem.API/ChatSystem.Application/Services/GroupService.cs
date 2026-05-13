using ChatSystem.Application.DTOs;
using ChatSystem.Application.Interfaces;
using ChatSystem.Application.Validators;
using ChatSystem.Domain.Entities;
using ChatSystem.Domain.Enums;
using ChatSystem.Domain.Exceptions;

namespace ChatSystem.Application.Services;

public sealed class GroupService
{
    private readonly IConversationRepository _conversationRepository;
    private readonly IUserRepository _userRepository;
    private readonly CreateGroupConversationValidator _validator;

    public GroupService(
        IConversationRepository conversationRepository,
        IUserRepository userRepository,
        CreateGroupConversationValidator validator)
    {
        _conversationRepository = conversationRepository;
        _userRepository = userRepository;
        _validator = validator;
    }

    // Create a Direct (1-to-1) conversation
    public async Task<ConversationDto> CreateDirectConversationAsync(
        CreateDirectConversationDto dto,
        CancellationToken cancellationToken = default)
    {
        if (dto.InitiatorUserId == Guid.Empty || dto.RecipientUserId == Guid.Empty)
            throw new DomainException("Both user IDs are required.");

        if (dto.InitiatorUserId == dto.RecipientUserId)
            throw new DomainException("A user cannot start a conversation with themselves.");

        // Guard: both users must exist.
        var initiatorExists = await _userRepository.ExistsAsync(
            dto.InitiatorUserId, cancellationToken);
        var recipientExists = await _userRepository.ExistsAsync(
            dto.RecipientUserId, cancellationToken);

        if (!initiatorExists)
            throw new DomainException($"Initiator user {dto.InitiatorUserId} not found.");
        if (!recipientExists)
            throw new DomainException($"Recipient user {dto.RecipientUserId} not found.");

        // ── Domain creates the conversation ───────────────────────────────────
        // Factory method enforces: Type=Direct, Title=null.
        var conversation = Conversation.CreateDirect(dto.InitiatorUserId);
        await _conversationRepository.AddAsync(conversation, cancellationToken);

        // ── Add both participants ─────────────────────────────────────────────
        // Domain factory: ConversationParticipant.Create enforces non-empty IDs
        // and sets JoinedAt = UtcNow.
        var initiatorParticipant = ConversationParticipant.Create(
            conversation.Id, dto.InitiatorUserId);
        var recipientParticipant = ConversationParticipant.Create(
            conversation.Id, dto.RecipientUserId);

        await _conversationRepository.AddParticipantAsync(
            initiatorParticipant, cancellationToken);
        await _conversationRepository.AddParticipantAsync(
            recipientParticipant, cancellationToken);

        return MapToDto(conversation);
    }

    public async Task<ConversationDto> CreateGroupConversationAsync(
        CreateGroupConversationDto dto,
        CancellationToken cancellationToken = default)
    {
        // ── Validate ──────────────────────────────────────────────────────────
        var validation = _validator.Validate(dto);
        if (!validation.IsValid)
            throw new DomainException(
                $"Invalid group conversation payload: {validation.ErrorSummary()}");

        // ── Guard: creator exists ─────────────────────────────────────────────
        var creatorExists = await _userRepository.ExistsAsync(
            dto.CreatedByUserId, cancellationToken);

        if (!creatorExists)
            throw new DomainException($"Creator user {dto.CreatedByUserId} not found.");

        // ── Guard: all invited participants exist ─────────────────────────────
        foreach (var participantId in dto.ParticipantIds)
        {
            var exists = await _userRepository.ExistsAsync(participantId, cancellationToken);
            if (!exists)
                throw new DomainException($"Participant user {participantId} not found.");
        }

        // ── Domain creates the conversation ───────────────────────────────────
        // Factory method enforces: Type=Group, Title required and <= 200 chars.
        var conversation = Conversation.CreateGroup(dto.CreatedByUserId, dto.Title);
        await _conversationRepository.AddAsync(conversation, cancellationToken);

        // ── Add creator as admin ──────────────────────────────────────────────
        var creatorParticipant = ConversationParticipant.Create(
            conversation.Id, dto.CreatedByUserId, isAdmin: true);
        await _conversationRepository.AddParticipantAsync(
            creatorParticipant, cancellationToken);

        // ── Add remaining participants ────────────────────────────────────────
        foreach (var userId in dto.ParticipantIds.Where(id => id != dto.CreatedByUserId))
        {
            var participant = ConversationParticipant.Create(conversation.Id, userId);
            await _conversationRepository.AddParticipantAsync(participant, cancellationToken);
        }

        return MapToDto(conversation);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Add a participant to an existing group
    // ─────────────────────────────────────────────────────────────────────────
    public async Task AddParticipantAsync(
        AddParticipantDto dto,
        CancellationToken cancellationToken = default)
    {
        if (dto.ConversationId == Guid.Empty || dto.UserIdToAdd == Guid.Empty)
            throw new DomainException("ConversationId and UserIdToAdd are required.");

        var conversation = await _conversationRepository
            .GetByIdAsync(dto.ConversationId, cancellationToken)
            ?? throw new DomainException($"Conversation {dto.ConversationId} not found.");

        // Only group conversations allow adding participants.
        if (conversation.Type == ConversationType.Direct)
            throw new DomainException("Cannot add participants to a direct conversation.");

        // Authorization: the requesting user must be an admin.
        var requestor = await _conversationRepository
            .GetParticipantAsync(dto.ConversationId, dto.RequestedByUserId, cancellationToken)
            ?? throw new DomainException("Requesting user is not a member of this conversation.");

        if (!requestor.IsAdmin)
            throw new DomainException("Only admins can add participants.");

        // Guard: user to add must exist.
        var userExists = await _userRepository.ExistsAsync(dto.UserIdToAdd, cancellationToken);
        if (!userExists)
            throw new DomainException($"User {dto.UserIdToAdd} not found.");

        // Guard: user must not already be a participant.
        var alreadyMember = await _conversationRepository
            .IsParticipantAsync(dto.ConversationId, dto.UserIdToAdd, cancellationToken);

        if (alreadyMember)
            throw new DomainException("User is already a participant of this conversation.");

        // Domain creates the participant record.
        var participant = ConversationParticipant.Create(
            dto.ConversationId, dto.UserIdToAdd);
        await _conversationRepository.AddParticipantAsync(participant, cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Remove a participant from a group
    // ─────────────────────────────────────────────────────────────────────────
    public async Task RemoveParticipantAsync(
        Guid conversationId,
        Guid requestedByUserId,
        Guid userIdToRemove,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository
            .GetByIdAsync(conversationId, cancellationToken)
            ?? throw new DomainException($"Conversation {conversationId} not found.");

        if (conversation.Type == ConversationType.Direct)
            throw new DomainException("Cannot remove participants from a direct conversation.");

        var requestor = await _conversationRepository
            .GetParticipantAsync(conversationId, requestedByUserId, cancellationToken)
            ?? throw new DomainException("Requesting user is not a member of this conversation.");

        // A non-admin can only remove themselves (leave group).
        if (!requestor.IsAdmin && requestedByUserId != userIdToRemove)
            throw new DomainException("Only admins can remove other participants.");

        // Guard: target must actually be a member.
        var isMember = await _conversationRepository
            .IsParticipantAsync(conversationId, userIdToRemove, cancellationToken);

        if (!isMember)
            throw new DomainException("Target user is not a participant of this conversation.");

        await _conversationRepository.RemoveParticipantAsync(
            conversationId, userIdToRemove, cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Rename a group conversation
    // ─────────────────────────────────────────────────────────────────────────
    public async Task RenameGroupAsync(
        Guid conversationId,
        Guid requestedByUserId,
        string newTitle,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository
            .GetByIdAsync(conversationId, cancellationToken)
            ?? throw new DomainException($"Conversation {conversationId} not found.");

        var requestor = await _conversationRepository
            .GetParticipantAsync(conversationId, requestedByUserId, cancellationToken)
            ?? throw new DomainException("Requesting user is not a member of this conversation.");

        if (!requestor.IsAdmin)
            throw new DomainException("Only admins can rename a group conversation.");

        // Domain method: validates length and guards against renaming Direct conversations.
        conversation.UpdateTitle(newTitle);
        await _conversationRepository.UpdateAsync(conversation, cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Get a user's conversation list
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<IReadOnlyList<ConversationDto>> GetUserConversationsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var userExists = await _userRepository.ExistsAsync(userId, cancellationToken);
        if (!userExists)
            throw new DomainException($"User {userId} not found.");

        var conversations = await _conversationRepository
            .GetByUserIdAsync(userId, cancellationToken);

        return conversations
            .Select(MapToDto)
            .ToList()
            .AsReadOnly();
    }

    private static ConversationDto MapToDto(Conversation c)
        => new()
        {
            Id = c.Id,
            Title = c.Title,
            Type = c.Type.ToString(),
            LastMessageAt = c.LastMessageAt
        };
}
