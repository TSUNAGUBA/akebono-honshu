namespace Akebono.Domain.Attendance;

/// <summary>
/// 直行/直帰申請のステータス (direct_requests.status)。多段承認のため
/// <see cref="InReview"/> (承認中) を持ち、申請者が取り下げられる <see cref="Withdrawn"/> も持つ
/// (打刻修正申請 <see cref="FixRequestStatus"/> に withdrawn が無いのと対照的)。
/// 値は DB 列 (SMALLINT) と 1:1。JSON へは camelCase 文字列 (pending / inReview / approved / rejected / withdrawn)。
/// </summary>
public enum DirectRequestStatus : short
{
    /// <summary>承認待ち (経路未設定 = 管理者単段のフォールバック時)。</summary>
    Pending = 0,
    /// <summary>承認中 (経路の途中ステップ)。</summary>
    InReview = 1,
    /// <summary>承認済み。</summary>
    Approved = 2,
    /// <summary>却下。</summary>
    Rejected = 3,
    /// <summary>取下げ (申請者本人のみ)。</summary>
    Withdrawn = 4,
}
