using Akebono.Domain.Common;

namespace Akebono.Domain.Entities;

/// <summary>
/// 関税率マスタ。海外からの仕入（輸入）時に適用する関税率(%) を、原産国 × 素材分類（甲皮／中底／底）ごとに
/// 正規管理する。商品新規ウィザード (/products/new) の⑤仕入単価で、仕入先の国と選択素材から関税を自動算出する。
///
/// 適用ロジック（フロント側 resolveCustomsDutyRate）:
///   - CountryId は必須（＝原産国）。素材分類 3 列は NULL 許容で、NULL は「その素材はワイルドカード（不問）」を表す。
///   - 選択素材の分類が、行の非 NULL 列と全て一致する行のみ「マッチ」とし、指定列数が最も多い（最も具体的な）行を採用する。
///   - DutyRate は従価税(%)。SpecificDutyPerPair は従量税(円/足、任意)。両者が併存する革製履物等では
///     「従価税額と従量税額の高い方」を関税額とする（日本の関税率表 第64類の運用に倣う）。
///
/// 実際の税率は 実行関税率表（税関）・EPA/特恵の適用可否・関税割当の一次/二次税率により変動するため、
/// シードは代表値（目安）とし、運用者が本マスタで各テナントの実態に合わせて登録・更新する。
/// </summary>
public class CustomsDutyRate : MasterEntityBase
{
    /// <summary>原産国（仕入先の国）。必須。</summary>
    public Guid CountryId { get; set; }

    /// <summary>甲皮素材の分類。NULL = すべての甲皮素材に適用（ワイルドカード）。</summary>
    public Guid? UpperMaterialClassificationId { get; set; }

    /// <summary>中底素材の分類。NULL = すべての中底素材に適用（ワイルドカード）。</summary>
    public Guid? InsoleMaterialClassificationId { get; set; }

    /// <summary>底素材の分類。NULL = すべての底素材に適用（ワイルドカード）。</summary>
    public Guid? OutsoleMaterialClassificationId { get; set; }

    /// <summary>従価税率(%)。例: 6.70 = 6.7%</summary>
    public decimal DutyRate { get; set; }

    /// <summary>従量税(円/足)。任意。設定時は「従価税額と従量税額の高い方」を関税額とする。</summary>
    public decimal? SpecificDutyPerPair { get; set; }

    public Country? Country { get; set; }
}
