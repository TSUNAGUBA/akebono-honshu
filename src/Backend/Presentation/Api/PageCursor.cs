using System.Text;
using Akebono.Application.Common;
using Microsoft.AspNetCore.WebUtilities;

namespace Akebono.Api;

/// <summary>
/// カーソル (キーセット) ページングの query 読取と不透明カーソルの符号化 (AKB-DOC-12 §7.1)。
///   - リクエスト: ?limit=50&amp;cursor=&lt;opaque&gt;。limit 既定 50・上限 200。
///   - カーソルは「createdAt の UTC Ticks | uuid」を base64url 化した不透明トークン。
///     クライアントは中身を解釈しない (契約はあくまで不透明文字列)。
///   - 不正 (limit 範囲外・カーソル復号不能) は 400 AKB-SYS-011 の DomainException を投げ、
///     中央 ApiExceptionMiddleware がエラー封筒へ変換する (IdempotencyKeyReader と同パターン)。
/// </summary>
public static class PageCursor
{
    /// <summary>
    /// endpoint のクエリバインド値 (?limit= / ?cursor=) を PageRequest へ変換する。
    /// 不正 (limit 範囲外・カーソル復号不能) は DomainException (400 AKB-SYS-011)。
    /// limit が数値ですらない場合はバインド段階の BadHttpRequestException を
    /// ApiExceptionMiddleware が 400 AKB-SYS-001 に封筒化する (型不正はボディ/クエリ共通の扱い)。
    /// </summary>
    public static PageRequest Read(int? limit, string? cursor)
    {
        var effectiveLimit = limit ?? PageRequest.DefaultLimit;
        if (effectiveLimit < 1 || effectiveLimit > PageRequest.MaxLimit)
            throw Invalid($"limit は 1〜{PageRequest.MaxLimit} の整数で指定してください");

        DateTime? afterCreatedAt = null;
        Guid? afterId = null;
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            (afterCreatedAt, afterId) = Decode(cursor);
        }

        return new PageRequest(effectiveLimit, afterCreatedAt, afterId);
    }

    /// <summary>キーセットアンカー (created_at, id) を不透明カーソルへ符号化する。</summary>
    public static string Encode(DateTime createdAtUtc, Guid id)
        => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes($"{createdAtUtc.Ticks}|{id}"));

    private static (DateTime AfterCreatedAt, Guid AfterId) Decode(string cursor)
    {
        try
        {
            var raw = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor));
            var parts = raw.Split('|');
            if (parts.Length == 2
                && long.TryParse(parts[0], out var ticks)
                && ticks >= DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks
                && Guid.TryParse(parts[1], out var id))
            {
                // 格納系列は TIMESTAMPTZ(UTC) のため Kind=Utc で復元する (Npgsql は Unspecified を拒否する)
                return (new DateTime(ticks, DateTimeKind.Utc), id);
            }
        }
        catch (FormatException)
        {
            // base64url 復号不能 → 下の Invalid へ
        }
        throw Invalid("cursor が不正です");
    }

    private static DomainException Invalid(string message)
        => new(AkbErrorCodes.SysPagingInvalid, StatusCodes.Status400BadRequest, message,
               userAction: "limit / cursor パラメータを確認して再実行してください");
}
