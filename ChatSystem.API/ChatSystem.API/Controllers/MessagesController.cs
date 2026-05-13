using System.Security.Claims;
using ChatSystem.Application.DTOs;
using ChatSystem.Application.Services;
using ChatSystem.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatSystem.API.Controllers;


[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/messages")]
[Authorize] // ALL endpoints require a valid JWT — no anonymous access
public sealed class MessagesController : ControllerBase
{
    private readonly ChatService _chatService;
    private readonly ILogger<MessagesController> _logger;

    public MessagesController(ChatService chatService, ILogger<MessagesController> logger)
    {
        _chatService = chatService;
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


    [HttpPost]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SendMessage(
        [FromBody] SendMessageDto dto,
        CancellationToken cancellationToken)
    {
        // Stamp the SenderId from the JWT — never trust the client to send their own ID.
        // If the DTO arrives with a SenderId field from the request body, it is
        // ignored here and overwritten with the authenticated user's identity.
        // This prevents a user from impersonating another sender.
        var callerId = GetCallerUserId();
        var request = dto with { SenderId = callerId };

        var message = await _chatService.SendMessageAsync(request, cancellationToken);

        // 201 Created with the new resource in the body.
        // Location header points to where the resource can be retrieved.
        return CreatedAtAction(
            actionName: nameof(GetMessages),
            routeValues: new { conversationId = message.ConversationId },
            value: message);
    }


    [HttpGet("conversations/{conversationId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<MessageResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMessages(
        [FromRoute] Guid conversationId,
        [FromQuery] DateTime? cursorSentAt,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        // Clamp pageSize: prevent a caller requesting 10,000 messages in one shot.
        pageSize = Math.Clamp(pageSize, 1, 100);

        var callerId = GetCallerUserId();

        var messages = await _chatService.GetMessagesAsync(
            conversationId,
            callerId,
            pageSize,
            cursorSentAt,
            cancellationToken);

        // Return the results with a custom header indicating cursor for next page.
        // The client uses the SentAt of the last item in the list as the next cursor.
        Response.Headers.Append("X-Page-Size", pageSize.ToString());
        Response.Headers.Append("X-Result-Count", messages.Count.ToString());

        return Ok(messages);
    }


    [HttpDelete("{messageId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteMessage(
        [FromRoute] Guid messageId,
        CancellationToken cancellationToken)
    {
        var callerId = GetCallerUserId();

        await _chatService.DeleteMessageAsync(messageId, callerId, cancellationToken);

        // 204 No Content: the operation succeeded but there is no body to return.
        // The message still exists in the DB (soft delete) — it just has IsDeleted=true.
        return NoContent();
    }

  
    [HttpPatch("conversations/{conversationId:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MarkAsRead(
        [FromRoute] Guid conversationId,
        CancellationToken cancellationToken)
    {
        var callerId = GetCallerUserId();

        await _chatService.MarkConversationAsReadAsync(
            conversationId, callerId, cancellationToken);

        return NoContent();
    }
}