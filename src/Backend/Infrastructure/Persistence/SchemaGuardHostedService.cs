using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Akebono.Infrastructure.Persistence;

/// <summary>
/// 起動時スキーマ検査。<b>このコード版が要求する勤怠スキーマ</b>が DB に反映済みかを
/// 起動時に 1 度だけ確認する。具体的には <c>users</c> の勤怠 6 列 (Iteration 30 で追加) と
/// <c>attendance_fix_requests.target_punch_id</c> (Iteration 31 / C-2 で追加) の存在を検査する。
///
/// <para>
/// 背景: <c>User</c> エンティティは勤怠 6 列 (<c>attendance_permission</c> 等) を map する。
/// コードが先にデプロイされ <c>db/migration/iter30-attendance.sql</c> が未適用のまま起動すると、
/// <c>users</c> を materialize する経路 — とりわけ<b>ログイン直後の <c>/auth/sync</c></b> — が
/// PostgreSQL の「column does not exist」で失敗し、<b>全利用者がログインできなくなる</b>。
/// その障害は「ログインだけ静かに失敗する」形で表面化し原因が分かりにくい。
/// 同様に <c>AttendanceFixRequest</c> は <c>target_punch_id</c> を map するため、
/// <c>db/migration/iter31-fix-target-punch.sql</c> が未適用だと打刻修正申請の一覧・作成・承認が
/// 全て「column does not exist」で失敗する (ログインは通るが機能単位で沈黙的に壊れる)。
/// </para>
///
/// <para>
/// 本ガードは、その状態を<b>起動失敗として即座に可視化</b>する (原則1: 必要な手順を obvious にする /
/// 原則7: 下位互換を破る変更を検知可能にする)。停止するのは「DB へ接続できたが必要な勤怠列が無い」=
/// スキーマ移行漏れが確実なときだけ。DB へ接続できない一過性エラー (RDS 起動待ち等) では
/// 停止せず警告のみで続行する (原則4)。接続回復後の各リクエストで自然に検証される。
/// <c>deploy/db/run-migrations.sh</c> は未適用の <c>db/migration/*.sql</c> をまとめて前進適用するため、
/// 検出された不足がどの iteration であっても復旧手順は 1 つ (ACTION=migrate) で足りる。
/// </para>
///
/// <para>
/// 新しい勤怠マイグレーションで「コードが map する列」を足したら、本ガードの検査対象にも
/// 追加すること (原則5: コードとガードの一貫性)。
/// </para>
///
/// <para>
/// <see cref="AuditPartitionMaintenanceService"/> と同じ <c>ExecuteSqlRawAsync</c> + スコープ生成の
/// パターンを用いる (原則3)。ただしこちらは起動をブロックすべきなので <see cref="IHostedService"/>
/// (StartAsync は serve 開始前に await される) を直接実装し、致命時は例外で起動を中断する。
/// </para>
/// </summary>
public sealed class SchemaGuardHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<SchemaGuardHostedService> logger) : IHostedService
{
    // コードが map する勤怠列 (テーブル.列) のいずれかが無ければ、不足を列挙して RAISE する。
    // ExecuteSqlRawAsync は本文字列をそのまま SQL として送る (パラメータ・プレースホルダなし)。
    // % は RAISE の書式指定でリテラル。information_schema.columns はテーブル自体が無い場合も
    // 行を返さない (= その列は不足扱い) ため、iter30 未適用でテーブルごと無い状態も検知できる。
    private const string GuardSql = @"
DO $$
DECLARE missing text;
BEGIN
  SELECT string_agg(label, ', ') INTO missing FROM (
    SELECT 'users.' || c AS label
      FROM unnest(ARRAY['attendance_permission','punch_required','attendance_rule_id','hire_date','weekly_days','weekly_hours']) AS c
     WHERE NOT EXISTS (
       SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'users' AND column_name = c)
    UNION ALL
    SELECT 'attendance_fix_requests.' || c AS label
      FROM unnest(ARRAY['target_punch_id']) AS c
     WHERE NOT EXISTS (
       SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'attendance_fix_requests' AND column_name = c)
  ) t;
  IF missing IS NOT NULL THEN
    RAISE EXCEPTION 'AKB-SCHEMA-GUARD: 勤怠スキーマの移行漏れです。不足列: %. 未適用の db/migration/*.sql を適用してください (ACTION=migrate deploy/db/run-migrations.sh が未適用分をまとめて前進適用します)。', missing;
  END IF;
END $$;";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AkebonoDbContext>();

        // 1. 接続確認。ここで失敗するのは一過性の可能性があるため起動は止めない (原則4)。
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "起動時スキーマ検査: DB へ接続できないため検査をスキップしました " +
                "(接続回復後の各リクエストで検証されます)");
            return;
        }

        // 2. 接続できた状態でのスキーマ検査。勤怠列が無ければ RAISE され例外になる。
        //    これは移行漏れが確実なので、明確なログを残して起動を中断する。
        try
        {
            await db.Database.ExecuteSqlRawAsync(GuardSql, cancellationToken);
            logger.LogInformation(
                "起動時スキーマ検査: users の勤怠列 (Iteration 30) と attendance_fix_requests.target_punch_id (Iteration 31) を確認しました");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex,
                "起動時スキーマ検査に失敗しました。勤怠マイグレーション (iter30-attendance.sql / iter31-fix-target-punch.sql) が未適用の可能性があります。" +
                "未適用のまま起動するとログイン (/auth/sync) や打刻修正申請が column does not exist で失敗するため、アプリの起動を中断します。" +
                "ACTION=migrate deploy/db/run-migrations.sh で適用してから再起動してください");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
