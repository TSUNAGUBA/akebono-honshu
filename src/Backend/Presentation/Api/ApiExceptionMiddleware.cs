using Akebono.Application.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Akebono.Api;

/// <summary>
/// 中央例外ハンドラ (AKB-DOC-12 §6.3)。想定内の業務エラー (DomainException) と
/// 代表的なインフラ例外をプラットフォーム標準のエラー封筒へ変換する。
/// 詳細な原因はレスポンスに載せず、traceId 付きでサーバーログに出す。
/// </summary>
public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (context.Response.HasStarted)
        {
            // レスポンス送出後は封筒を書けない。ログのみ残して再スロー (接続は打ち切られる)。
            logger.LogError(ex, "レスポンス開始後の例外 (traceId={TraceId})", context.TraceIdentifier);
            throw;
        }
        catch (DomainException ex)
        {
            if (ex.StatusCode >= 500)
                logger.LogError(ex, "業務エラー {Code} (traceId={TraceId})", ex.Code, context.TraceIdentifier);
            else
                logger.LogInformation("業務エラー {Code}: {Message} (traceId={TraceId})",
                    ex.Code, ex.Message, context.TraceIdentifier);
            await WriteAsync(context, ex.StatusCode,
                ApiEnvelope.ErrorBody(context, ex.Code, ex.Message, ex.UserAction, ex.Details));
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(ex, "楽観ロック競合 (traceId={TraceId})", context.TraceIdentifier);
            await WriteAsync(context, StatusCodes.Status409Conflict,
                ApiEnvelope.ErrorBody(context, AkbErrorCodes.SysOptimisticConflict,
                    "他の更新と競合しました。最新の状態を取得して再実行してください",
                    userAction: "画面を再読込して再実行してください"));
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            logger.LogWarning(ex, "一意制約違反 (traceId={TraceId})", context.TraceIdentifier);
            await WriteAsync(context, StatusCodes.Status409Conflict,
                ApiEnvelope.ErrorBody(context, AkbErrorCodes.SysUniqueViolation,
                    "同一のキーを持つデータが既に存在します",
                    userAction: "入力内容の重複を確認してください"));
        }
        catch (BadHttpRequestException ex)
        {
            logger.LogInformation(ex, "リクエスト不正 (traceId={TraceId})", context.TraceIdentifier);
            await WriteAsync(context, StatusCodes.Status400BadRequest,
                ApiEnvelope.ErrorBody(context, AkbErrorCodes.SysMalformedBody,
                    "リクエストの形式が不正です"));
        }
        catch (ArgumentException ex)
        {
            // DomainException 未変換の検証系 throw のフォールバック (旧規約: ArgumentException = 422)
            logger.LogInformation(ex, "検証エラー (traceId={TraceId})", context.TraceIdentifier);
            await WriteAsync(context, StatusCodes.Status422UnprocessableEntity,
                ApiEnvelope.ErrorBody(context, AkbErrorCodes.SysValidation, ex.Message));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "未分類の内部エラー (traceId={TraceId})", context.TraceIdentifier);
            await WriteAsync(context, StatusCodes.Status500InternalServerError,
                ApiEnvelope.ErrorBody(context, AkbErrorCodes.SysInternal,
                    "内部エラーが発生しました",
                    userAction: "時間をおいて再試行し、解消しない場合は traceId を添えて管理者へ連絡してください"));
        }
    }

    private static async Task WriteAsync(HttpContext context, int statusCode, object body)
    {
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(body);
    }
}
