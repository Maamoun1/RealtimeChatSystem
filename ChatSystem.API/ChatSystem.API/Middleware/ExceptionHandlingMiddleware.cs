using System.Net;
using System.Text.Json;
using ChatSystem.Domain.Exceptions;

namespace ChatSystem.API.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    // Reuse a single JsonSerializerOptions instance — Options objects are
    // expensive to create and safe to share across threads.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, message) = MapException(exception);

        // Log with appropriate severity.
        // DomainExceptions are expected (bad input) — log as Warning, not Error.
        // Unexpected exceptions are logged as Error with full stack trace.
        if (statusCode >= 500)
        {
            _logger.LogError(
                exception,
                "Unhandled exception on {Method} {Path}",
                context.Request.Method,
                context.Request.Path);
        }
        else
        {
            _logger.LogWarning(
                "Domain/client exception on {Method} {Path}: {Message}",
                context.Request.Method,
                context.Request.Path,
                exception.Message);
        }

        // Build the structured error response.
        var errorResponse = new ErrorResponse(statusCode, message);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        var json = JsonSerializer.Serialize(errorResponse, JsonOptions);
        await context.Response.WriteAsync(json);
    }

    private static (int statusCode, string message) MapException(Exception exception)
        => exception switch
        {
            // Business rule violation — always surfaced to the caller.
            DomainException domainEx
                => ((int)HttpStatusCode.BadRequest, domainEx.Message),

            // Authentication failures — do not reveal why auth failed.
            UnauthorizedAccessException
                => ((int)HttpStatusCode.Unauthorized, "Authentication is required."),

            // Entity lookup failures from Application layer.
            KeyNotFoundException keyEx
                => ((int)HttpStatusCode.NotFound, keyEx.Message),

            // Client disconnected mid-request — not a server error.
            OperationCanceledException
                => (499, "The request was cancelled by the client."),

            // Catch-all — never reveal internal error details to the caller.
            _ => ((int)HttpStatusCode.InternalServerError,
                    "An unexpected error occurred. Please try again later.")
        };
}


public sealed record ErrorResponse(int StatusCode, string Message);