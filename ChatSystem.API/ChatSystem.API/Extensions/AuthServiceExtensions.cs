using System.Text;
using ChatSystem.API.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace ChatSystem.API.Extensions;


public static class AuthServiceExtensions
{
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind and validate JwtSettings at startup.
        // If Key, Issuer, or Audience is missing the app fails immediately
        // rather than producing invalid tokens silently.
        var jwtSettings = configuration
            .GetSection(JwtSettings.SectionName)
            .Get<JwtSettings>()
            ?? throw new InvalidOperationException(
                $"'{JwtSettings.SectionName}' configuration section is missing.");

        if (string.IsNullOrWhiteSpace(jwtSettings.Key))
            throw new InvalidOperationException("JWT Key must not be empty.");

        if (jwtSettings.Key.Length < 32)
            throw new InvalidOperationException(
                "JWT Key must be at least 32 characters (256 bits) for HMAC-SHA256.");

        // Register JwtSettings for injection into AuthService (Phase 5).
        services.Configure<JwtSettings>(
            configuration.GetSection(JwtSettings.SectionName));

        var signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtSettings.Key));

        services
            .AddAuthentication(options =>
            {
                // Make JWT Bearer the default scheme for both authentication and challenge.
                // Without this, ASP.NET Core defaults to cookie auth and returns
                // a 302 redirect instead of 401 for unauthenticated API requests.
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    // ── The four required validations ─────────────────────────
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,

                    ValidateIssuer = true,
                    ValidIssuer = jwtSettings.Issuer,

                    ValidateAudience = true,
                    ValidAudience = jwtSettings.Audience,

                    ValidateLifetime = true,
                    // ClockSkew: allow 0 seconds of drift.
                    // Default is 5 minutes — a generous window that lets expired
                    // tokens work longer than expected. Set to zero in production.
                    ClockSkew = TimeSpan.Zero,

                    // The claim that maps to HttpContext.User.Identity.Name
                    // and is used as the user identifier throughout the app.
                    NameClaimType = "sub"
                };

                options.Events = new JwtBearerEvents
                {
                    // ── SignalR token extraction ───────────────────────────────
                    // WebSocket and SSE connections cannot send custom headers.
                    // SignalR clients put the JWT in the query string:
                    //   wss://api/chathub?access_token=eyJ...
                    // This hook reads it and moves it to the Authorization header
                    // so the standard JWT middleware validates it normally.
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;

                        if (!string.IsNullOrEmpty(accessToken) &&
                            path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },

                    // ── Auth failure logging ───────────────────────────────────
                    // Logs the reason for token rejection at Debug level.
                    // Useful during development; harmless in production.
                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<JwtBearerEvents>>();

                        logger.LogDebug(
                            "JWT authentication failed: {Reason}",
                            context.Exception.Message);

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();

        return services;
    }
}