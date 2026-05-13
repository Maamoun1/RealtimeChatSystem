using System.Security.Claims;
using ChatSystem.Application.DTOs;
using ChatSystem.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatSystem.API.Controllers;


[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/conversations")]
[Authorize]
public sealed class ConversationsController : ControllerBase
{
    private readonly GroupService _groupService;
    private readonly ILogger<ConversationsController> _logger;

    public ConversationsController(
        GroupService groupService,
        ILogger<ConversationsController> logger)
    {
        _groupService = groupService;
        _logger = logger;
    }

    private Guid GetCallerUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(sub) || !Guid.TryParse(sub, out var userId))
            throw new UnauthorizedAccessException(
                "User identity could not be resolved from token.");

        return userId;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/v1/conversations
    // Return the caller's inbox — all conversations sorted by recent activity
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all conversations the authenticated user participates in,
    /// ordered by LastMessageAt DESC (most recent activity first).
    /// This is the inbox view — loaded when the user opens the app.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ConversationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyConversations(CancellationToken cancellationToken)
    {
        var callerId = GetCallerUserId();
        var conversations = await _groupService
            .GetUserConversationsAsync(callerId, cancellationToken);

        return Ok(conversations);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/v1/conversations/direct
    // Start or retrieve a 1-to-1 conversation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a Direct (1-to-1) conversation between the caller and a recipient.
    /// Idempotent: if a direct conversation already exists between these two users,
    /// the existing one is returned (GroupService handles this).
    /// Returns 201 Created with the conversation.
    /// </summary>
    [HttpPost("direct")]
    [ProducesResponseType(typeof(ConversationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateDirectConversation(
        [FromBody] CreateDirectConversationDto dto,
        CancellationToken cancellationToken)
    {
        var callerId = GetCallerUserId();

        // Stamp the initiator from the JWT — never let the client set this.
        var request = dto with { InitiatorUserId = callerId };

        var conversation = await _groupService
            .CreateDirectConversationAsync(request, cancellationToken);

        return CreatedAtAction(
            actionName: nameof(GetMyConversations),
            routeValues: null,
            value: conversation);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/v1/conversations/group
    // Create a named group conversation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new Group conversation. The caller becomes the first admin.
    /// Returns 201 Created with the new conversation.
    /// </summary>
    [HttpPost("group")]
    [ProducesResponseType(typeof(ConversationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateGroupConversation(
        [FromBody] CreateGroupConversationDto dto,
        CancellationToken cancellationToken)
    {
        var callerId = GetCallerUserId();
        var request = dto with { CreatedByUserId = callerId };

        var conversation = await _groupService
            .CreateGroupConversationAsync(request, cancellationToken);

        return CreatedAtAction(
            actionName: nameof(GetMyConversations),
            routeValues: null,
            value: conversation);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/v1/conversations/participants
    // Add a participant to a group conversation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a user to a group conversation. The caller must be an admin.
    /// DomainException is thrown (→ 400) if:
    ///   - The conversation is a Direct conversation
    ///   - The caller is not an admin
    ///   - The user to add is already a participant
    /// Returns 204 No Content on success.
    /// </summary>
    [HttpPost("participants")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AddParticipant(
        [FromBody] AddParticipantDto dto,
        CancellationToken cancellationToken)
    {
        var callerId = GetCallerUserId();
        var request = dto with { RequestedByUserId = callerId };

        await _groupService.AddParticipantAsync(request, cancellationToken);

        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE /api/v1/conversations/{conversationId}/participants/{userId}
    // Remove a participant from a group conversation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes a participant from a group conversation.
    /// Admins may remove any member. Regular members may only remove themselves (leave).
    /// Returns 204 No Content on success.
    /// </summary>
    [HttpDelete("{conversationId:guid}/participants/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RemoveParticipant(
        [FromRoute] Guid conversationId,
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        var callerId = GetCallerUserId();

        await _groupService.RemoveParticipantAsync(
            conversationId, callerId, userId, cancellationToken);

        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PATCH /api/v1/conversations/{conversationId}/rename
    // Rename a group conversation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renames a group conversation. Caller must be an admin.
    /// Domain guard: Direct conversations cannot be renamed (DomainException → 400).
    /// Returns 204 No Content on success.
    /// </summary>
    [HttpPatch("{conversationId:guid}/rename")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RenameGroup(
        [FromRoute] Guid conversationId,
        [FromBody] RenameGroupRequest request,
        CancellationToken cancellationToken)
    {
        var callerId = GetCallerUserId();

        await _groupService.RenameGroupAsync(
            conversationId, callerId, request.NewTitle, cancellationToken);

        return NoContent();
    }
}

/// <summary>
/// Request body for the rename endpoint.
/// A dedicated record prevents over-posting: only NewTitle can be set from the request.
///
/// WHY not inline this in ConversationsController?
/// Record declarations inside a class cause C# compiler warnings in some targets.
/// Keeping it adjacent to the controller file is clean and discoverable.
/// </summary>
public sealed record RenameGroupRequest(string NewTitle);