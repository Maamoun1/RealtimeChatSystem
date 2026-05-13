using ChatSystem.Application.Services;

namespace ChatSystem.API.Extensions;

// <summary>
// Registers all Application layer services into the DI container.

public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services)
    {
        // Scoped: services orchestrate a single use case per request.
        // They depend on Scoped repositories — must be at most Scoped.
        services.AddScoped<ChatService>();
        services.AddScoped<GroupService>();
        services.AddScoped<PresenceService>();

        return services;
    }
}