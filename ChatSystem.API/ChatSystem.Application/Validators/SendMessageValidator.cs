using ChatSystem.Application.DTOs;

namespace ChatSystem.Application.Validators;


public sealed class ValidationFailure
{
    public string PropertyName { get; }
    public string ErrorMessage { get; }

    public ValidationFailure(string propertyName, string errorMessage)
    {
        PropertyName = propertyName;
        ErrorMessage = errorMessage;
    }

    public override string ToString() => $"{PropertyName}: {ErrorMessage}";
}


public sealed class ValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<ValidationFailure> Errors { get; } = new();

    public void AddError(string propertyName, string errorMessage)
        => Errors.Add(new ValidationFailure(propertyName, errorMessage));

    /// <summary>
    /// Convenience: produces a single human-readable error string for logging.
    /// </summary>
    public string ErrorSummary()
        => string.Join("; ", Errors.Select(e => e.ToString()));
}


public sealed class SendMessageValidator
{
    private const int MaxBodyLength = 4000;

    public ValidationResult Validate(SendMessageDto dto)
    {
        var result = new ValidationResult();

        if (dto is null)
        {
            result.AddError(nameof(dto), "Request body cannot be null.");
            return result; // Short-circuit — nothing else to validate
        }

        // ── ConversationId ────────────────────────────────────────────────────
        if (dto.ConversationId == Guid.Empty)
            result.AddError(
                nameof(dto.ConversationId),
                "ConversationId must be a valid non-empty GUID.");

        // ── SenderId ──────────────────────────────────────────────────────────
        if (dto.SenderId == Guid.Empty)
            result.AddError(
                nameof(dto.SenderId),
                "SenderId must be a valid non-empty GUID.");

        // ── Body ──────────────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(dto.Body))
        {
            result.AddError(
                nameof(dto.Body),
                "Message body cannot be empty or whitespace.");
        }
        else if (dto.Body.Length > MaxBodyLength)
        {
            result.AddError(
                nameof(dto.Body),
                $"Message body cannot exceed {MaxBodyLength} characters. " +
                $"Received {dto.Body.Length} characters.");
        }

        return result;
    }
}

public sealed class CreateGroupConversationValidator
{
    public ValidationResult Validate(CreateGroupConversationDto dto)
    {
        var result = new ValidationResult();

        if (dto is null)
        {
            result.AddError(nameof(dto), "Request body cannot be null.");
            return result;
        }

        if (dto.CreatedByUserId == Guid.Empty)
            result.AddError(
                nameof(dto.CreatedByUserId),
                "CreatedByUserId must be a valid non-empty GUID.");

        if (string.IsNullOrWhiteSpace(dto.Title))
            result.AddError(
                nameof(dto.Title),
                "Group conversation title cannot be empty.");

        if (dto.Title?.Length > 200)
            result.AddError(
                nameof(dto.Title),
                "Group conversation title cannot exceed 200 characters.");

        return result;
    }
}