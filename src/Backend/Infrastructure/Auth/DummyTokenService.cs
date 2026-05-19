using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Akebono.Application.Auth;
using Microsoft.Extensions.Configuration;

namespace Akebono.Infrastructure.Auth;

/// <summary>
/// Iteration 0 専用のダミートークン実装。固定 HMAC secret で署名するだけの簡易 JWT 風。
/// Iteration 4 Hardening で Firebase Admin SDK 検証に置換予定。
/// </summary>
public class DummyTokenService(IConfiguration config) : ITokenService
{
    private readonly string _secret = config["DummyAuth:Secret"]
        ?? throw new InvalidOperationException("DummyAuth:Secret 未設定");

    private record Payload(long Sub, string LoginId, long ExpUnix);

    public string IssueToken(long userId, string loginId)
    {
        var payload = new Payload(userId, loginId, DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeSeconds());
        var json = JsonSerializer.Serialize(payload);
        var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(json));
        var sig = ComputeSignature(payloadB64);
        return $"{payloadB64}.{sig}";
    }

    public bool TryValidate(string token, out long userId)
    {
        userId = 0;
        if (string.IsNullOrEmpty(token)) return false;

        var parts = token.Split('.');
        if (parts.Length != 2) return false;

        var expected = ComputeSignature(parts[0]);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(parts[1]))) return false;

        try
        {
            var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
            var payload = JsonSerializer.Deserialize<Payload>(json);
            if (payload is null) return false;
            if (payload.ExpUnix < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return false;
            userId = payload.Sub;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string ComputeSignature(string payloadB64)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadB64));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }
}
