using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Akebono.Application.Common;

namespace Akebono.Api;

/// <summary>
/// 作成系 POST の Idempotency-Key ヘッダ読取 (AKB-DOC-12 §8)。
/// ヘッダ欠如は 400 AKB-SYS-004。ペイロードの SHA-256 を併せて返し、
/// 「同一キー・異なるペイロード」(409 AKB-SYS-005) の検出はサービス層が行う。
/// </summary>
public static class IdempotencyKeyReader
{
    public const string HeaderName = "Idempotency-Key";
    private const int MaxKeyLength = 128;

    /// <summary>ヘッダ値と要求ペイロードのハッシュを読み取る。不正時は DomainException。</summary>
    public static (string Key, string PayloadHash) Read(HttpContext http, object payload)
    {
        if (!http.Request.Headers.TryGetValue(HeaderName, out var values)
            || string.IsNullOrWhiteSpace(values.ToString()))
        {
            throw new DomainException(
                AkbErrorCodes.SysIdempotencyKeyMissing, StatusCodes.Status400BadRequest,
                "作成系リクエストには Idempotency-Key ヘッダが必須です",
                userAction: "クライアントを最新化するか、一意な Idempotency-Key を付与して再実行してください");
        }

        var key = values.ToString().Trim();
        if (key.Length > MaxKeyLength)
        {
            throw DomainException.Validation(
                $"Idempotency-Key は {MaxKeyLength} 文字以内で指定してください");
        }

        var json = JsonSerializer.Serialize(payload);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        return (key, hash);
    }
}
