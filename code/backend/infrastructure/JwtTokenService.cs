using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ballast.Core;
using Ballast.Core.Domain;

namespace Ballast.Infrastructure;

public sealed record TokenClaims(
    string Subject, string Email, string Name, UserRole Role, string Type, DateTimeOffset ExpiresAt, string TokenId);

public sealed class JwtTokenService(BallastSettings settings, TimeProvider clock)
{
    public const string AccessType = "access";
    public const string RefreshType = "refresh";

    private static string B64(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromB64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Convert.FromBase64String(s);
    }

    private byte[] Sign(string signingInput) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.JwtSecret), Encoding.UTF8.GetBytes(signingInput));

    public string Issue(User user, string type, TimeSpan lifetime)
    {
        var now = clock.GetUtcNow();
        var header = new JsonObject { ["alg"] = "HS256", ["typ"] = "JWT" };
        var payload = new JsonObject
        {
            ["sub"] = user.Id,
            ["email"] = user.Email,
            ["name"] = user.Name,
            ["role"] = user.Role.ToString(),
            ["typ"] = type,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.Add(lifetime).ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N")
        };
        string input = $"{B64(Encoding.UTF8.GetBytes(header.ToJsonString()))}.{B64(Encoding.UTF8.GetBytes(payload.ToJsonString()))}";
        return $"{input}.{B64(Sign(input))}";
    }

    public TokenClaims? Validate(string token, string expectedType)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return null;
            var header = JsonNode.Parse(FromB64(parts[0]));
            if (header?["alg"]?.GetValue<string>() != "HS256") return null;
            if (!CryptographicOperations.FixedTimeEquals(Sign($"{parts[0]}.{parts[1]}"), FromB64(parts[2]))) return null;
            var p = JsonNode.Parse(FromB64(parts[1]));
            if (p is null || p["typ"]?.GetValue<string>() != expectedType) return null;
            var exp = DateTimeOffset.FromUnixTimeSeconds(p["exp"]!.GetValue<long>());
            if (exp <= clock.GetUtcNow()) return null;
            return new TokenClaims(
                p["sub"]!.GetValue<string>(), p["email"]!.GetValue<string>(), p["name"]!.GetValue<string>(),
                Enum.Parse<UserRole>(p["role"]!.GetValue<string>()), expectedType, exp, p["jti"]!.GetValue<string>());
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            return null;
        }
    }
}
