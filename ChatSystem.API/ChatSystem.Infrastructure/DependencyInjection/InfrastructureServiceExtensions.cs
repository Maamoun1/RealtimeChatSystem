using ChatSystem.Application.Interfaces;
using ChatSystem.Infrastructure.Caching;
using ChatSystem.Infrastructure.Messaging;
using ChatSystem.Infrastructure.Persistence;
using ChatSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using StackExchange.Redis;

namespace ChatSystem.Infrastructure.DependencyInjection;

/// <summary>
/// Extension method that registers every Infrastructure dependency into the DI container.
///
/// Called once from Program.cs:
///   builder.Services.AddInfrastructure(builder.Configuration);
///
/// Design decisions:
/// - AppDbContext is registered as Scoped (default for AddDbContext). Each HTTP
///   request / SignalR hub invocation gets its own DbContext instance, which means
///   its own Unit of Work. Multiple repository operations in one request share the
///   same context and participate in the same transaction boundary.
/// - IConnectionMultiplexer (Redis) is Singleton. The multiplexer manages a
///   connection pool internally. Creating one per request would open thousands of
///   connections under load — a classic misconfiguration. One multiplexer, many
///   logical databases.
/// - IConnection (RabbitMQ) is Singleton for the same reason — connections are
///   expensive; channels are cheap. The service creates one channel from the
///   singleton connection.
/// - Repositories are Scoped — they depend on AppDbContext (Scoped), so they
///   must be at most Scoped. Never register a Scoped service as Singleton.
/// </summary>
public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddDatabase(configuration)
            .AddRepositories()
            .AddRedis(configuration)
            .AddRabbitMq(configuration);

        return services;
    }

    // ── Database ──────────────────────────────────────────────────────────────

    private static IServiceCollection AddDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("SqlServer")
            ?? throw new InvalidOperationException(
                "Connection string 'SqlServer' is missing from configuration.");

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                // Retry on transient SQL errors (connection blips, deadlocks).
                // EnableRetryOnFailure uses SQL Server's built-in transient error list.
                sqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null);

                // Warn when a single query takes longer than 30 seconds.
                // Does NOT cancel the query — adjust CommandTimeout for that.
                sqlOptions.CommandTimeout(30);

                // Migrations assembly: if Infrastructure is a separate project,
                // EF Core needs to know where to find migration classes.
                sqlOptions.MigrationsAssembly(
                    typeof(AppDbContext).Assembly.FullName);
            });

            // In development: log SQL queries to the console.
            // In production this should be removed or gated behind IsDevelopment().
            options.EnableSensitiveDataLogging(false);
            options.EnableDetailedErrors(false);
        });

        return services;
    }

    // ── Repositories ──────────────────────────────────────────────────────────

    private static IServiceCollection AddRepositories(
        this IServiceCollection services)
    {
        // Each interface → its concrete implementation.
        // Scoped: same instance within a request, new instance per request.
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();

        return services;
    }

    // ── Redis ─────────────────────────────────────────────────────────────────

    private static IServiceCollection AddRedis(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException(
                "Connection string 'Redis' is missing from configuration.");

        // Singleton: IConnectionMultiplexer is thread-safe and designed to be
        // shared across the entire application lifetime.
        services.AddSingleton<IConnectionMultiplexer>(
            _ => ConnectionMultiplexer.Connect(connectionString));

        // Scoped: RedisCacheService itself is stateless after construction —
        // it could be Singleton too, but Scoped keeps the lifetime consistent
        // with the repositories it is used alongside.
        services.AddScoped<ICacheService, RedisCacheService>();

        return services;
    }

    // ── RabbitMQ ──────────────────────────────────────────────────────────────

    private static IServiceCollection AddRabbitMq(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Read RabbitMQ config from appsettings
        var rabbitSection = configuration.GetSection("RabbitMq");

        var factory = new ConnectionFactory
        {
            HostName = rabbitSection["Host"] ?? "localhost",
            Port = int.Parse(rabbitSection["Port"] ?? "5672"),
            UserName = rabbitSection["Username"] ?? "guest",
            Password = rabbitSection["Password"] ?? "guest",
            VirtualHost = rabbitSection["VHost"] ?? "/",

            // Automatic recovery: if the broker restarts, the client reconnects.
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
        };

        // Singleton connection — expensive to create, thread-safe, reusable.
        services.AddSingleton<IConnection>(_ => factory.CreateConnection());

        // Singleton service — it holds a channel (cheap, created from the connection).
        // RabbitMqMessageQueueService.Dispose() will close channel + connection cleanly.
        services.AddSingleton<IMessageQueueService, RabbitMqMessageQueueService>();

        return services;
    }
}