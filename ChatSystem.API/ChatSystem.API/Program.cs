using ChatSystem.API.Extensions;
using ChatSystem.API.Hubs;
using ChatSystem.API.Middleware;
using ChatSystem.Infrastructure.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;


var builder = WebApplication.CreateBuilder(args);

// ── 1. Controllers ────────────────────────────────────────────────────────────
builder.Services.AddControllers(options =>
{
    
    options.SuppressAsyncSuffixInActionNames = false;
})
.ConfigureApiBehaviorOptions(options =>
{
    // Disable the automatic 400 response triggered by [ApiController] on model
    // validation failure. We handle validation ourselves via DomainException
    // and the middleware — one consistent error shape throughout.
    options.SuppressModelStateInvalidFilter = true;
});

// ── 2. API versioning ──────────────────────────────────────────────────────────

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true; // Adds api-supported-versions header
});

// ── 3. Swagger (OpenAPI) ──────────────────────────────────────ش─────────────────
builder.Services.AddSwaggerDocumentation();

// ── 4. JWT Authentication ─────────────────────────────────────────────────────
// Extension method in AuthServiceExtensions.cs.
// Validates JwtSettings at startup — fails fast if Key is missing.
builder.Services.AddJwtAuthentication(builder.Configuration);

// ── 5. CORS ────────────────────────────────────────────────────────────────────

builder.Services.AddCors(options =>
{
    options.AddPolicy("ChatSystemCors", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader();
        }
        else
        {
            // Production: lock down to known origins.
            // Replace with real domain(s) before deploying.
            var allowedOrigins = builder.Configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>() ?? Array.Empty<string>();

            policy
                .WithOrigins(allowedOrigins)
                .AllowAnyMethod()
                .AllowAnyHeader()
                // WHY AllowCredentials: SignalR WebSocket connections require
                // credentials to be explicitly allowed alongside specific origins.
                // Cannot combine AllowAnyOrigin with AllowCredentials.
                .AllowCredentials();
        }
    });
});

// ── 6. SignalR ────────────────────────────────────────────────────────────────
// WHY AddSignalR here and not in Infrastructure?
// SignalR is a transport concern owned by the API layer.
// The ChatHub (implemented in the next step) lives in the API layer.
//
// AddJsonProtocol: camelCase property names match the JavaScript client convention.
// EnableDetailedErrors: ON in development so hub exceptions appear in the browser console.
builder.Services
    .AddSignalR(hubOptions =>
    {
        hubOptions.EnableDetailedErrors = builder.Environment.IsDevelopment();
        hubOptions.KeepAliveInterval = TimeSpan.FromSeconds(15);
        hubOptions.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
        hubOptions.MaximumReceiveMessageSize = 32 * 1024; // 32KB — generous for chat messages
    })
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.CamelCase;
    })
// WHY AddStackExchangeRedis (SignalR backplane)?
// When this API runs on multiple server instances behind a load balancer,
// a message sent to Hub instance A must reach clients connected to Hub instance B.
// The Redis backplane uses pub/sub to broadcast across all instances.
// Without this, users on different server instances cannot see each other's messages.
.AddStackExchangeRedis(
    builder.Configuration.GetConnectionString("Redis")
        ?? throw new InvalidOperationException("Redis connection string is missing."),
    options =>
    {
        options.Configuration.AbortOnConnectFail = false;

        options.Configuration.ChannelPrefix =
            new StackExchange.Redis.RedisChannel(
                "ChatSystem",
                StackExchange.Redis.RedisChannel.PatternMode.Literal);
    });

// ── 7. Application services ───────────────────────────────────────────────────
// Registers ChatService, GroupService, PresenceService.
builder.Services.AddApplicationServices();

// ── 8. Infrastructure layer ───────────────────────────────────────────────────
// Registers DbContext, repositories, Redis cache, RabbitMQ.
// One call — all of Infrastructure is wired by its own extension method.
builder.Services.AddInfrastructure(builder.Configuration);

// ── 9. HttpContextAccessor ────────────────────────────────────────────────────
// Required to access HttpContext.User inside services if needed.
// Prefer injecting ClaimsPrincipal directly in controllers rather than
// using IHttpContextAccessor in services — keeps services testable.
builder.Services.AddHttpContextAccessor();


var app = builder.Build();

// ── Middleware 1: Global exception handler ─────────────────────────────────
// MUST be first. Wraps everything — catches exceptions from any downstream middleware.
app.UseMiddleware<ExceptionHandlingMiddleware>();

// ── Middleware 2: HTTPS redirection ───────────────────────────────────────
// Redirect HTTP → HTTPS before any processing. Security baseline.
app.UseHttpsRedirection();

// ── Middleware 3: Swagger ─────────────────────────────────────────────────
app.UseSwaggerDocumentation(app.Environment);

// ── Middleware 4: CORS ────────────────────────────────────────────────────

app.UseCors("ChatSystemCors");

// ── Middleware 5: Authentication ──────────────────────────────────────────

app.UseAuthentication();

// ── Middleware 6: Authorization ───────────────────────────────────────────
app.UseAuthorization();

// ── Middleware 7: Controller endpoints ────────────────────────────────────
app.MapControllers();

// ── Middleware 8: SignalR hubs ────────────────────────────────────────────
// All clients connect to: wss://host/hubs/chat?access_token=...
// The [Authorize] attribute on ChatHub + the OnMessageReceived hook in
// AuthServiceExtensions handle token extraction from the query string.
app.MapHub<ChatHub>("/hubs/chat");

// ── Health check endpoint ─────────────────────────────────────────────────
// Simple liveness probe — returns 200 OK if the app is running.
// Used by Docker / Kubernetes readiness checks.
app.MapGet("/health", () => Results.Ok(new
{
    Status = "Healthy",
    Timestamp = DateTime.UtcNow,
    Service = "RealtimeChatSystem"
})).AllowAnonymous();

app.Run();

// Make Program class accessible for integration tests (WebApplicationFactory).
public partial class Program { }