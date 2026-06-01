namespace Akebono.Application.Masters;

/// <summary>マスタ一覧表示用の汎用 DTO (Code + Name + 状態のみ、共通画面で利用)。</summary>
public record MasterListItem(
    long Id,
    string Code,
    string Name,
    bool DeleteFlag,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>マスタ作成・更新の共通ペイロード (Code + Name)。拡張カラムは継承で拡張。</summary>
public record MasterWriteRequest(string Code, string Name);

/// <summary>マスタ参照件数 (Phase 6 F-20 usage API)。Iteration 1 では参照テーブルなしのため常に 0、Iteration 2 で実装。</summary>
public record MasterUsage(long Id, int ReferenceCount, string[] ReferringTables);
