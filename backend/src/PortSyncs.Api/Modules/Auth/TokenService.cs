using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace PortSyncs.Api.Modules.Auth;

/// <summary>
/// Short-lived access tokens (JWT, 15 min) and long-lived rotating refresh tokens (30 days).
/// Organization roles are NOT put in the token: they are checked against the database on
/// each request, so removing someone from a company takes effect immediately.
/// </summary>
public sealed class TokenService(AuthOptions options)
{
    private readonly byte[] _key = options.SigningKeyBytes();

    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(Guid userId, string phone)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(options.AccessTokenMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = userId.ToString(),
                ["phone"] = phone,
            },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(_key), SecurityAlgorithms.HmacSha256),
        };
        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidIssuer = options.Issuer,
        ValidAudience = options.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(_key),
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromSeconds(30),
    };

    /// <summary>A random URL-safe refresh token; only its SHA-256 hash is stored.</summary>
    public static string NewRefreshToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string HashRefreshToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>6-digit login code.</summary>
    public static string NewOtpCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    /// <summary>HMAC of phone + code with the server key, so a database leak does not reveal codes.</summary>
    public string HashOtp(string phone, string code)
    {
        using var hmac = new HMACSHA256(_key);
        return Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{phone}:{code}")));
    }

    public bool OtpMatches(string phone, string code, string storedHash) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(HashOtp(phone, code)),
            Encoding.ASCII.GetBytes(storedHash));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
