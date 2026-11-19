using System.Text;

namespace PortSyncs.Api.Modules.Auth;

/// <summary>Bound from the "Auth" section (Auth__JwtSigningKey, ...).</summary>
public sealed class AuthOptions
{
    /// <summary>At least 32 bytes. Generate with: openssl rand -base64 48</summary>
    public string JwtSigningKey { get; set; } = "";
    public string Issuer { get; set; } = "portsyncs";
    public string Audience { get; set; } = "portsyncs-apps";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
    public int OtpMinutes { get; set; } = 5;
    public int OtpMaxAttempts { get; set; } = 5;
    public int OtpMaxPerWindow { get; set; } = 3;
    public int OtpWindowMinutes { get; set; } = 15;

    public byte[] SigningKeyBytes()
    {
        var bytes = Encoding.UTF8.GetBytes(JwtSigningKey);
        if (bytes.Length < 32)
            throw new InvalidOperationException("Auth__JwtSigningKey must be at least 32 bytes. Generate one with: openssl rand -base64 48");
        return bytes;
    }
}
