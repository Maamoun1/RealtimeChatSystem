using ChatSystem.Application.Interfaces;
using Microsoft.EntityFrameworkCore.Storage;
using StackExchange.Redis;
using System.Text.Json;

namespace ChatSystem.Infrastructure.Caching;


public sealed class RedisCacheService : ICacheService
{
    private readonly StackExchange.Redis.IDatabase _database;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public RedisCacheService(IConnectionMultiplexer connectionMultiplexer)
    {
        // GetDatabase() returns a lightweight proxy — not a new connection.
        // Default database (index 0) is used; pass an index if multi-tenancy is needed.
        _database = connectionMultiplexer.GetDatabase();
    }

    // ── Get ───────────────────────────────────────────────────────────────────

    public async Task<T?> GetAsync<T>(
        string key,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var value = await _database.StringGetAsync(key);

        if (!value.HasValue || value.IsNullOrEmpty)
            return null;

        // For plain strings (e.g., presence flag "1", last-seen ISO timestamp),
        // if T is string we can return the value directly without deserialising.
        if (typeof(T) == typeof(string))
            return value.ToString() as T;

        return JsonSerializer.Deserialize<T>(value.ToString(), JsonOptions);
    }

    // ── Set ───────────────────────────────────────────────────────────────────

    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        string serialized = typeof(T) == typeof(string)
            ? value.ToString()!                                        // avoid wrapping "1" as "\"1\""
            : JsonSerializer.Serialize(value, JsonOptions);

        await _database.StringSetAsync(
            key,
            serialized,
            expiry,
            When.Always); // Always overwrite — SET semantics
    }

    // ── Remove ────────────────────────────────────────────────────────────────

    public async Task RemoveAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        // KeyDeleteAsync returns false if the key didn't exist — that's fine.
        // The contract is idempotent: after this call the key does not exist.
        await _database.KeyDeleteAsync(key);
    }

    // ── Exists ────────────────────────────────────────────────────────────────

    public async Task<bool> ExistsAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        // KeyExistsAsync: O(1) — does not transfer the value, just checks presence.
        return await _database.KeyExistsAsync(key);
    }
}