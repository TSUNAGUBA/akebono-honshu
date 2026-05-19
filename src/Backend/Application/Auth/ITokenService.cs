namespace Akebono.Application.Auth;

public interface ITokenService
{
    /// <summary>Iteration 0 ではダミー実装、Iteration 4 Hardening で Firebase Admin SDK 検証に置換</summary>
    string IssueToken(long userId, string loginId);

    /// <summary>Iteration 0 ではダミー検証 (固定 secret HMAC)、Iteration 4 で Firebase ID Token 検証に置換</summary>
    bool TryValidate(string token, out long userId);
}
