namespace ChatSystem.API.Settings;


public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

 
    public string Key { get; init; } = string.Empty;

    // The "iss" claim value. Identifies who issued the token (this API).
   
    public string Issuer { get; init; } = string.Empty;

    // The "aud" claim value. Identifies the intended recipients.
    // Example: "https://realtimechat.com"

    public string Audience { get; init; } = string.Empty;

    // Access token lifetime in minutes. Short-lived by design (15–60 min).
    // After expiry, the client uses the refresh token to get a new access token.
  
    public int AccessTokenExpiryMinutes { get; init; } = 60;

    // Refresh token lifetime in days. Longer-lived (7–30 days).
    // Stored server-side in Redis or DB to allow revocation.
    public int RefreshTokenExpiryDays { get; init; } = 7;
}