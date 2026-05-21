using Akebono.Application.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Akebono.Infrastructure.Storage;

/// <summary>
/// 開発環境用 (dev): wwwroot/{key} に書込み、`UseStaticFiles()` 経由で配信。
/// URL は HttpContext から absolute URL を組み立てる (Frontend と Backend が別 origin の場合に対応)。
/// </summary>
public sealed class LocalImageStorage : IImageStorageService
{
    private readonly IWebHostEnvironment _env;
    private readonly IHttpContextAccessor _httpContext;

    public LocalImageStorage(IWebHostEnvironment env, IHttpContextAccessor httpContext)
    {
        _env = env;
        _httpContext = httpContext;
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var absPath = Path.Combine(webRoot, key.Replace('/', Path.DirectorySeparatorChar));
        var dirAbs = Path.GetDirectoryName(absPath)
            ?? throw new InvalidOperationException($"key '{key}' から保存先ディレクトリを解決できません");
        Directory.CreateDirectory(dirAbs);

        await using var fs = new FileStream(absPath, FileMode.CreateNew);
        await content.CopyToAsync(fs, ct);
    }

    public Task<string> GetUrlAsync(string key, CancellationToken ct)
    {
        var req = _httpContext.HttpContext?.Request
            ?? throw new InvalidOperationException("LocalImageStorage.GetUrlAsync は HTTP request context が必要");
        var origin = $"{req.Scheme}://{req.Host.Value}{req.PathBase.Value}";
        return Task.FromResult($"{origin}/{key}");
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var absPath = Path.Combine(webRoot, key.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(absPath)) File.Delete(absPath);
        return Task.CompletedTask;
    }
}
