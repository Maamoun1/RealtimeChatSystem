using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ChatSystem.API.Settings;
using ChatSystem.Application.Interfaces;
using ChatSystem.Domain.Entities;
using ChatSystem.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Org.BouncyCastle.Crypto.Generators;

namespace ChatSystem.API.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
// Request / Response records
//
// WHY dedicated records and not using DTOs from Application layer?
// Auth request shapes (LoginRequest, RegisterRequest) are API contracts.
// They contain fields the API cares about (Password in plain text — never
// reaches the domain). Using Application DTOs here would mean the Application
// layer carries knowledge of passwords, which it should not.
// ─────────────────────────────────────────────────────────────────────────────

public sealed record RegisterRequest(
    string Username,
    string Email,
    string Password,
    string DisplayName);

public sealed record LoginRequest(
    string Email,
    string Password);

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    Guid UserId,
    string DisplayName);

public sealed record RefreshRequest(string RefreshToken);

/// <summary>
/// Handles user registration, login, and token refresh.
///
/// WHAT this controller does:
/// - Accepts credentials, validates them, issues JWT + refresh token pairs.
/// - All heavy lifting (hashing, DB lookup, token storage) lives in services.
///
/// WHY token generation lives temporarily here:
/// A dedicated AuthService (Phase 5) will encapsulate JWT generation, refresh
/// token rotation, and Redis-based revocation. For now, JWT generation is
/// implemented directly in this controller using the infrastructure that already
/// exists (IUserRepository + JwtSettings). The structure matches what AuthService
/// will look like — migration is a refactor, not a rewrite.
///
/// IMPORTANT password security note:
/// BCrypt (via BCrypt.Net-Next NuGet) is used for password hashing.
/// Never store plain text passwords. Never use MD5 or SHA1 for passwords.
/// BCrypt has a built-in work factor that makes brute-force expensive.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IUserRepository userRepository,
        IOptions<JwtSettings> jwtSettings,
        ILogger<AuthController> logger)
    {
        _userRepository = userRepository;
        _jwtSettings = jwtSettings.Value;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/v1/auth/register
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a new user and returns a JWT + refresh token pair.
    /// On success: 201 Created with AuthResponse.
    /// On duplicate email/username: 400 (DB unique constraint → DomainException via middleware).
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        // Basic null guard — FluentValidation will replace this in Phase 5.
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.Username))
            throw new DomainException("Email, username, and password are required.");

        if (request.Password.Length < 8)
            throw new DomainException("Password must be at least 8 characters.");

        // Hash password BEFORE passing to domain — the domain stores hashes, not passwords.
        // BCrypt.Net-Next: work factor 12 is the current production recommendation.
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12);

        // Domain factory: validates Username/Email/DisplayName lengths and formats.
        var user = new User(
            username: request.Username,
            email: request.Email,
            passwordHash: passwordHash,
            displayName: request.DisplayName);

        await _userRepository.AddAsync(user, cancellationToken);

        _logger.LogInformation("New user registered: {UserId} ({Email})", user.Id, user.Email);

        var response = IssueTokenPair(user);

        return CreatedAtAction(nameof(Register), response);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/v1/auth/login
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Authenticates a user with email + password and returns a JWT + refresh token.
    ///
    /// WHY the same error message for "user not found" and "wrong password"?
    /// Returning "email not found" vs "wrong password" is a user enumeration
    /// vulnerability — attackers can discover which emails are registered.
    /// Always return the same vague message for any auth failure.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        const string genericAuthError = "Invalid email or password.";

        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);

        // Deliberate: same error for "not found" and "wrong password".
        if (user is null)
            return Unauthorized(new { message = genericAuthError });

        var passwordValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
        if (!passwordValid)
            return Unauthorized(new { message = genericAuthError });

        _logger.LogInformation("User logged in: {UserId}", user.Id);

        return Ok(IssueTokenPair(user));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/v1/auth/refresh
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Exchanges a valid refresh token for a new access token + refresh token pair
    /// (refresh token rotation).
    ///
    /// PLACEHOLDER: Full implementation requires Redis-backed refresh token
    /// storage (Phase 5 — AuthService). The structure here shows the correct
    /// contract. In production:
    ///   1. Validate the refresh token exists in Redis (not expired, not revoked)
    ///   2. Delete the old token (rotation: one-time use)
    ///   3. Issue a new access + refresh pair
    ///   4. Store the new refresh token in Redis
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Refresh([FromBody] RefreshRequest request)
    {
        // TODO (Phase 5): validate refresh token against Redis store.
        // For now, return a structured placeholder that shows the correct shape.
        return Unauthorized(new
        {
            message = "Refresh token validation requires AuthService (Phase 5)."
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/v1/auth/logout
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Revokes the caller's refresh token. The access token is short-lived and
    /// self-expires — it cannot be revoked (stateless JWT).
    ///
    /// PLACEHOLDER: Requires Redis refresh token store in Phase 5.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Logout()
    {
        // TODO (Phase 5): delete refresh token from Redis.
        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private: JWT + refresh token generation
    //
    // WHY here and not in a service?
    // This logic will move to AuthService in Phase 5. For now it lives as a
    // private method — self-contained and easy to extract. No business logic;
    // pure token mechanics.
    // ─────────────────────────────────────────────────────────────────────────

    private AuthResponse IssueTokenPair(User user)
    {
        var accessToken = GenerateAccessToken(user);
        var refreshToken = GenerateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpiryMinutes);

        return new AuthResponse(
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            ExpiresAt: expiresAt,
            UserId: user.Id,
            DisplayName: user.DisplayName);
    }

    private string GenerateAccessToken(User user)
    {
        var signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_jwtSettings.Key));

        var credentials = new SigningCredentials(
            signingKey, SecurityAlgorithms.HmacSha256);

        // Claims embedded in the JWT payload.
        // "sub" (subject) = the user's ID — the standard claim for identity.
        // ClaimTypes.NameIdentifier maps to "sub" in GetCallerUserId().
        // Additional claims (roles, permissions) can be added here later.
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub,   user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()), // unique token ID
            new Claim("displayName",                 user.DisplayName)
        };

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateRefreshToken()
    {
        // 64 bytes of cryptographically secure random data, base64-encoded.
        // This is opaque to the client — they store it and send it back.
        // Never use Guid.NewGuid() for refresh tokens — GUIDs are not cryptographically random.
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }
}