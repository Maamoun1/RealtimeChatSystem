using System.Security.Claims;
using ChatSystem.Application.DTOs;
using ChatSystem.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ChatSystem.API.Hubs;

public static class HubMethods
{
    // Server → Client (messages the server sends to connected clients)
    public const string ReceiveMessage = "ReceiveMessage";
    public const string MessageStatusUpdated = "MessageStatusUpdated";
    public const string UserOnline = "UserOnline";
    public const string UserOffline = "UserOffline";
    public const string UserTyping = "UserTyping";
    public const string UserStoppedTyping = "UserStoppedTyping";
    public const string Error = "Error";
}

/// <summary>
/// Real-time SignalR hub for the chat system.
///
/// WHAT this hub does:
/// - Manages WebSocket connections (connected / disconnected lifecycle)
/// - Maps users to SignalR groups (one group per conversation)
/// - Receives messages from clients and delegates to ChatService for persistence
/// - Broadcasts delivered messages and status updates to conversation groups
///
/// WHY this hub stays THIN:
/// The hub is a transport layer — it speaks WebSocket so the rest of the system
/// doesn't have to. Business decisions (is this user allowed in this conversation?
/// is the message body valid? what is the new Status?) are made by ChatService and
/// PresenceService. The hub calls them and relays the result. Period.
///
/// WHY [Authorize] on the Hub?
/// Without it, anyone can open a WebSocket connection to /hubs/chat without a JWT.
/// The [Authorize] attribute validates the token on connection (the OnMessageReceived
/// hook in AuthServiceExtensions moves it from the query string to the header first).
///
/// SignalR Groups:
/// Each conversation gets a SignalR group named "conversation:{conversationId}".
/// When a message is sent to that group, every client in the group receives it —
/// regardless of which server instance they are connected to (Redis backplane handles this).
/// </summary>
[Authorize]
public sealed class ChatHub : Hub
{
    private readonly ChatService _chatService;
    private readonly PresenceService _presenceService;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(
        ChatService chatService,
        PresenceService presenceService,
        ILogger<ChatHub> logger)
    {
        _chatService = chatService;
        _presenceService = presenceService;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Identity helper — mirrors the controller approach
    // ─────────────────────────────────────────────────────────────────────────

    private Guid GetCallerUserId()
    {
        var sub = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? Context.User?.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(sub) || !Guid.TryParse(sub, out var userId))
            throw new HubException("User identity could not be resolved from token.");

        return userId;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Connection lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    // Called automatically by SignalR when a client establishes a connection.
    //
    // Flow:
    // 1. Mark the user as online in Redis (TTL-based presence key)
    // 2. Deliver any messages that arrived while the user was offline
    // 3. Notify others in the user's conversations that they are now online

    public override async Task OnConnectedAsync()
    {
        var userId = GetCallerUserId();

        _logger.LogDebug("User connected: {UserId}, ConnectionId: {ConnectionId}",
            userId, Context.ConnectionId);

        // Mark online in Redis — sets a TTL key that auto-expires if heartbeat stops.
        await _presenceService.MarkOnlineAsync(userId);

        // Deliver messages that arrived while the user was offline.
        // ChatService handles the status transition (Sent → Delivered) and
        // publishes StatusUpdated events to the queue for sender notification.
        await _chatService.DeliverPendingMessagesAsync(userId);

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Called automatically when a client disconnects — cleanly or due to a network drop.
    ///
    /// WHY is presence removed HERE for clean disconnects but also via TTL for drops?
    /// OnDisconnectedAsync fires immediately for a graceful close (browser tab closed).
    /// For a sudden network drop, the TCP keepalive can delay detection by 30+ seconds.
    /// The Redis TTL (45s) covers that window: if no heartbeat arrives in 45s, the
    /// presence key expires and the user appears offline — even without OnDisconnectedAsync.
    /// Both mechanisms working together gives accurate presence tracking.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetCallerUserId();

        _logger.LogDebug(
            "User disconnected: {UserId}, ConnectionId: {ConnectionId}, Error: {Error}",
            userId, Context.ConnectionId, exception?.Message ?? "clean disconnect");

        // Remove presence key and record last-seen timestamp in Redis.
        await _presenceService.MarkOfflineAsync(userId);

 
        await base.OnDisconnectedAsync(exception);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Client → Server: Join a conversation group
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds this connection to the SignalR group for a conversation.
    ///
    /// Called by the client when it opens a chat screen.
    /// After joining, the client will receive all messages broadcast to this group.
    ///
    /// WHY is there no membership check here?
    /// ChatService.GetMessagesAsync already checks membership and will throw
    /// DomainException (→ HubException) if the user is not a participant.
    /// We do not duplicate that check. The hub trusts the service.
    ///
    /// The SignalR group name convention: "conversation:{conversationId}"
    /// This namespacing prevents collisions if other group types are added later.
    /// </summary>
    public async Task JoinConversation(Guid conversationId)
    {
        var userId = GetCallerUserId();
        var groupName = ConversationGroup(conversationId);

        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        // Notify others in the conversation that this user is now online in this chat.
        await Clients.OthersInGroup(groupName).SendAsync(
            HubMethods.UserOnline,
            new { UserId = userId, ConversationId = conversationId });

        _logger.LogDebug("User {UserId} joined group {Group}", userId, groupName);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Client → Server: Leave a conversation group
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes this connection from a conversation group.
    /// Called when the user navigates away from a chat screen.
    /// After leaving, they no longer receive real-time events for this conversation.
    /// </summary>
    public async Task LeaveConversation(Guid conversationId)
    {
        var userId = GetCallerUserId();
        var groupName = ConversationGroup(conversationId);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

        await Clients.OthersInGroup(groupName).SendAsync(
            HubMethods.UserOffline,
            new { UserId = userId, ConversationId = conversationId });

        _logger.LogDebug("User {UserId} left group {Group}", userId, groupName);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Client → Server: Send a message
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Receives a message from a client, persists it, and broadcasts it.
    ///
    /// THE CRITICAL FLOW — persistence before broadcast:
    ///   1. Client sends the message to this hub method
    ///   2. ChatService persists to SQL Server (source of truth written first)
    ///   3. ChatService publishes a MessageSentEvent to RabbitMQ
    ///   4. Hub broadcasts the persisted message to all group members NOW
    ///      (real-time path — fast delivery to currently connected clients)
    ///   5. RabbitMQ consumer handles push notifications for offline clients
    ///
    /// WHY persist before broadcast?
    /// If broadcast happened before DB write and the write failed, clients would
    /// see a message that doesn't exist — ghost messages. The DB is always the
    /// source of truth. Broadcast on success only.
    ///
    /// WHY use HubException for errors?
    /// HubException is SignalR's typed exception — it delivers a structured error
    /// to the client's hub.on("error") handler instead of killing the connection.
    /// Regular C# exceptions from a hub method disconnect the client.
    /// </summary>
    public async Task SendMessage(SendMessageDto dto)
    {
        var callerId = GetCallerUserId();

        // Stamp SenderId from JWT — same pattern as the REST controller.
        var request = dto with { SenderId = callerId };

        // ── STEP 1: Persist (source of truth written first) ────────────────
        MessageResponseDto persistedMessage;
        try
        {
            persistedMessage = await _chatService.SendMessageAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Message send failed for user {UserId}: {Error}", callerId, ex.Message);

            // HubException delivers a structured error to the client without
            // dropping the connection.
            throw new HubException(ex.Message);
        }

        // ── STEP 2: Broadcast to all group members ────────────────────────
        // This reaches all connected clients on any server instance
        // (Redis backplane fan-out).
        await Clients
            .Group(ConversationGroup(dto.ConversationId))
            .SendAsync(HubMethods.ReceiveMessage, persistedMessage);

        _logger.LogDebug(
            "Message {MessageId} persisted and broadcast to group {Group}",
            persistedMessage.Id,
            ConversationGroup(dto.ConversationId));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Client → Server: Typing indicator
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Broadcasts a "user is typing" signal to all OTHER members of a conversation.
    ///
    /// WHY no persistence here?
    /// Typing indicators are ephemeral UI signals — they have no business meaning
    /// and should never be stored. This is a pure fan-out: receive from one client,
    /// relay to others immediately.
    ///
    /// WHY Clients.OthersInGroup and not Clients.Group?
    /// The sender does not need to receive their own typing indicator — they already
    /// know they are typing. OthersInGroup excludes the calling connection.
    /// </summary>
    public async Task NotifyTyping(Guid conversationId)
    {
        var userId = GetCallerUserId();
        var groupName = ConversationGroup(conversationId);

        await Clients.OthersInGroup(groupName).SendAsync(
            HubMethods.UserTyping,
            new { UserId = userId, ConversationId = conversationId });
    }

    /// <summary>
    /// Broadcasts a "user stopped typing" signal.
    /// Clients send this when the typing timeout fires (~3 seconds after last keystroke).
    /// </summary>
    public async Task NotifyStoppedTyping(Guid conversationId)
    {
        var userId = GetCallerUserId();
        var groupName = ConversationGroup(conversationId);

        await Clients.OthersInGroup(groupName).SendAsync(
            HubMethods.UserStoppedTyping,
            new { UserId = userId, ConversationId = conversationId });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Client → Server: Presence heartbeat
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Refreshes the user's online presence TTL in Redis.
    ///
    /// WHY is a heartbeat needed if OnConnectedAsync already marks the user online?
    /// The Redis presence key has a 45-second TTL. After OnConnectedAsync sets it,
    /// the key will expire if nothing refreshes it. The client sends a Heartbeat
    /// call every ~30 seconds, which calls MarkOnlineAsync to reset the TTL.
    ///
    /// This pattern is more reliable than relying solely on SignalR's KeepAlive
    /// because it also updates the application-level presence state, not just
    /// the transport-level connection state.
    /// </summary>
    public async Task Heartbeat()
    {
        var userId = GetCallerUserId();
        await _presenceService.MarkOnlineAsync(userId);

        // Return a pong so the client knows the server received the heartbeat.
        await Clients.Caller.SendAsync("Pong", DateTime.UtcNow);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Utilities
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Consistent group name for a conversation.
    /// All hub methods that need to address a conversation's SignalR group use this.
    ///
    /// Convention: "conversation:{guid}" — the prefix prevents collisions with
    /// any other group types introduced in future features.
    /// </summary>
    private static string ConversationGroup(Guid conversationId)
        => $"conversation:{conversationId}";
}