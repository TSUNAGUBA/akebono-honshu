using Akebono.Application.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Akebono.Infrastructure.Storage;

/// <summary>
/// 開発環境用 (dev): wwwroot/{key} に書込み、`UseStaticFiles()` 経由で配信。
///
/// URL 解決の優先順位 (Iter 4 段階 C-1 reviewer 指摘 M4 / LSP 違反対応):
/// 1. appsettings `ImageStorage:LocalPublicBaseUrl` (例: `http://localhost:5000`) があればそれを優先
/// 2. HTTP request context があれば `HttpContext.Request.Scheme + Host` から組立
/// 3. どちらも無ければ `InvalidOperationException` (バックグラウンドジョブ等から呼ぶには 1 の設定が必要)
/// </summary>
public sealed class LocalImageStorage : IImageStorageService
{
    private readonly IWebHostEnvironment _env;
    private readonly IHttpContextAccessor _httpContext;
    private readonly string? _configuredBaseUrl;

    public LocalImageStorage(
        IWebHostEnvironment env,
        IHttpContextAccessor httpContext,
        IConfiguration config)
    {
        _env = env;
        _httpContext = httpContext;
        _configuredBaseUrl = config["ImageStorage:LocalPublicBaseUrl"]?.TrimEnd('/');
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        ValidateKey(key);
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var absPath = ResolveAbsolutePath(webRoot, key);
        var dirAbs = Path.GetDirectoryName(absPath)
            ?? throw new InvalidOperationException($"key '{key}' から保存先ディレクトリを解決できません");
        Directory.CreateDirectory(dirAbs);

        await using var fs = new FileStream(absPath, FileMode.CreateNew);
        await content.CopyToAsync(fs, ct);
    }

    public Task<string> GetUrlAsync(string key, CancellationToken ct)
    {
        ValidateKey(key);
        _ = ct;
        if (!string.IsNullOrEmpty(_configuredBaseUrl))
            return Task.FromResult($"{_configuredBaseUrl}/{key}");

        var req = _httpContext.HttpContext?.Request
            ?? throw new InvalidOperationException(
                "LocalImageStorage.GetUrlAsync: HTTP request context が無く、`ImageStorage:LocalPublicBaseUrl` も未設定。" +
                "バックグラウンドジョブ等から呼ぶ場合は appsettings に `ImageStorage:LocalPublicBaseUrl` を設定してください。");
        var origin = $"{req.Scheme}://{req.Host.Value}{req.PathBase.Value}";
        return Task.FromResult($"{origin}/{key}");
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        ValidateKey(key);
        _ = ct;
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var absPath = ResolveAbsolutePath(webRoot, key);
        if (File.Exists(absPath)) File.Delete(absPath);
        return Task.CompletedTask;
    }

    /// <summary>
    /// path traversal 防御 (Iter 4 段階 C-1 reviewer 指摘 M5)。
    /// key に `..` を含むか rooted path だと `webRoot` 外への書込が可能になるため早期 throw。
    /// 加えて canonical path で webRoot prefix 検証を `ResolveAbsolutePath` で行う。
    /// </summary>
    private static void ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("key が空です", nameof(key));
        if (key.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException($"key に '..' は含められません: '{key}'", nameof(key));
        if (Path.IsPathRooted(key))
            throw new ArgumentException($"key は相対パスである必要があります: '{key}'", nameof(key));
    }

    private static string ResolveAbsolutePath(string webRoot, string key)
    {
        var combined = Path.Combine(webRoot, key.Replace('/', Path.DirectorySeparatorChar));
        var canonical = Path.GetFullPath(combined);
        var canonicalRoot = Path.GetFullPath(webRoot);
        // 末尾区切りを正規化して prefix containment を確認 (例: webRoot 兄弟ディレクトリへの逃げ防止)。
        var rootWithSep = canonicalRoot.EndsWith(Path.DirectorySeparatorChar)
            ? canonicalRoot : canonicalRoot + Path.DirectorySeparatorChar;
        if (!canonical.StartsWith(rootWithSep, StringComparison.Ordinal))
            throw new ArgumentException($"key の解決先が webRoot 外: '{key}' → '{canonical}'", nameof(key));
        return canonical;
    }
}
