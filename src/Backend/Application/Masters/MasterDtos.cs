namespace Akebono.Application.Masters;

/// <summary>マスタ作成・更新の共通ペイロード (Code + Name)。拡張カラムは継承で拡張。</summary>
public record MasterWriteRequest(string Code, string Name);

/// <summary>マスタ参照件数 (Phase 6 F-20 usage API)。Iteration 1 では参照テーブルなしのため常に 0、Iteration 2 で実装。</summary>
public record MasterUsage(Guid Id, int ReferenceCount, string[] ReferringTables);
