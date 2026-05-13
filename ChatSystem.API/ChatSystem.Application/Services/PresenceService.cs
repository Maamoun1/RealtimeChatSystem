using ChatSystem.Application.Interfaces;

namespace ChatSystem.Application.Services;


public sealed class PresenceService
{
    // Key template — kept as a constant to prevent typos across the codebase.
    private const string PresenceKeyPrefix = "user:presence:";
    private const string LastSeenKeyPrefix = "user:lastseen:";

    // A user is considered online as long as they send a heartbeat within this window.
    private static readonly TimeSpan OnlineTtl = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan LastSeenTtl = TimeSpan.FromDays(30);

    private readonly ICacheService _cache;

    public PresenceService(ICacheService cache)
    {
        _cache = cache;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Mark online — called on SignalR connection and on heartbeat
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates or refreshes the presence key for a user.
    /// Called by the SignalR Hub on OnConnectedAsync and on each heartbeat ping.
    /// The TTL resets on every call — if no heartbeat arrives within 45 seconds,
    /// the key expires and the user is effectively offline.
    /// </summary>
    public async Task MarkOnlineAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var key = PresenceKey(userId);

        // Value is a simple flag — existence of the key signals online status.
        // We store "1" rather than the full user object to keep memory usage minimal.
        await _cache.SetAsync(key, "1", OnlineTtl, cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Mark offline — called on SignalR disconnection
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Immediately removes the presence key and records the last-seen timestamp.
    /// Called by the SignalR Hub on OnDisconnectedAsync.
    /// Recording last-seen allows the UI to show "last seen 5 minutes ago".
    /// </summary>
    public async Task MarkOfflineAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await _cache.RemoveAsync(PresenceKey(userId), cancellationToken);

        // Store last-seen timestamp separately with a long TTL so the UI can
        // display it even days after the user was last active.
        await _cache.SetAsync(
            LastSeenKey(userId),
            DateTime.UtcNow.ToString("O"), // ISO 8601 round-trip format
            LastSeenTtl,
            cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Query presence
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when the user has an active, non-expired presence key.
    /// </summary>
    public async Task<bool> IsOnlineAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _cache.ExistsAsync(PresenceKey(userId), cancellationToken);

    /// <summary>
    /// Returns the last-seen UTC timestamp for a user, or null if not recorded.
    /// Null typically means the user has never connected, or the key has expired.
    /// </summary>
    public async Task<DateTime?> GetLastSeenAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var raw = await _cache.GetAsync<string>(LastSeenKey(userId), cancellationToken);

        if (raw is null)
            return null;

        return DateTime.TryParse(raw, out var dt) ? dt : null;
    }

    /// <summary>
    /// Returns the online status for a batch of user IDs in parallel.
    /// Used to populate the participant list in a conversation detail view.
    /// Parallel fan-out is safe because each key is independent.
    /// </summary>
    public async Task<Dictionary<Guid, bool>> GetOnlineStatusBatchAsync(
        IEnumerable<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        // Run all cache lookups in parallel — avoids sequential round-trips.
        var tasks = userIds.Select(async id =>
            new KeyValuePair<Guid, bool>(id, await IsOnlineAsync(id, cancellationToken)));

        var results = await Task.WhenAll(tasks);

        return results.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Key helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static string PresenceKey(Guid userId) => $"{PresenceKeyPrefix}{userId}";
    private static string LastSeenKey(Guid userId) => $"{LastSeenKeyPrefix}{userId}";
}
