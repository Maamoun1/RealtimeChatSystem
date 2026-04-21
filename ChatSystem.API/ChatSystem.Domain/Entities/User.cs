using ChatSystem.Domain.Exceptions;

namespace ChatSystem.Domain.Entities;


public class User
{


    public Guid Id { get; private set; }
    public string Username { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;
    public string? AvatarUrl { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public User(string username, string email, string passwordHash, string displayName)
    {
        Id = Guid.NewGuid();
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;

        SetUsername(username);
        SetEmail(email);
        SetPasswordHash(passwordHash);
        SetDisplayName(displayName);
    }

    private User() { }

    public void UpdateDisplayName(string displayName)
    {
        SetDisplayName(displayName);
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateAvatarUrl(string? avatarUrl)
    {
        // No validation: null is valid (remove avatar), any non-empty string is valid.
        // URL format validation is an application / infrastructure concern.
        AvatarUrl = avatarUrl?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdatePasswordHash(string newHash)
    {
        SetPasswordHash(newHash);
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        if (!IsActive)
            throw new DomainException("User is already inactive.");

        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        if (IsActive)
            throw new DomainException("User is already active.");

        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }


    private void SetUsername(string username)
    {
        username = username?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(username))
            throw new DomainException("Username cannot be empty.");

        if (username.Length > 50)
            throw new DomainException("Username cannot exceed 50 characters.");

        Username = username;
    }

    private void SetEmail(string email)
    {
        email = email?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(email))
            throw new DomainException("Email cannot be empty.");

        if (email.Length > 255)
            throw new DomainException("Email cannot exceed 255 characters.");

        // Basic structural check — format validation belongs in the Application layer
        // using FluentValidation, but a gross sanity check here prevents storing "@"
        // or "notanemail" in the database.
        if (!email.Contains('@'))
            throw new DomainException("Email is not valid.");

        Email = email;
    }

    private void SetPasswordHash(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            throw new DomainException("Password hash cannot be empty.");

        PasswordHash = hash;
    }

    private void SetDisplayName(string displayName)
    {
        displayName = displayName?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(displayName))
            throw new DomainException("Display name cannot be empty.");

        if (displayName.Length > 100)
            throw new DomainException("Display name cannot exceed 100 characters.");

        DisplayName = displayName;
    }
}