using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.IO;

namespace EcommercePortfolio.Infrastructure.Storage;

public class LocalImageStorage : IImageStorage
{
    private const long MaxBytes = 5 * 1024 * 1024;

    private static readonly HashSet<string> AllowedMime = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp"
    };

    private readonly IWebHostEnvironment _env;
    private readonly ILogger<LocalImageStorage> _log;

    public LocalImageStorage(IWebHostEnvironment env, ILogger<LocalImageStorage> log)
    {
        _env = env;
        _log = log;
    }

    public async Task<ImageStoreResult> StoreAsync(long productId, IFormFile file, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return new ImageStoreResult(false, Error: "Empty file.");

        if (file.Length > MaxBytes)
            return new ImageStoreResult(false, Error: $"File exceeds {MaxBytes / 1024 / 1024} MB limit.");

        if (!AllowedMime.Contains(file.ContentType))
            return new ImageStoreResult(false, Error: "Only JPEG, PNG, or WebP images are allowed.");

        var relDir = Path.Combine("uploads", "products", productId.ToString());
        var absDir = Path.Combine(_env.WebRootPath, relDir);
        Directory.CreateDirectory(absDir);

        var ext = file.ContentType switch
        {
            "image/png"  => ".png",
            "image/webp" => ".webp",
            _            => ".jpg"
        };
        var fileName = $"{Guid.NewGuid():N}{ext}";
        var absPath = Path.Combine(absDir, fileName);

        await using (var stream = File.Create(absPath))
        {
            await file.CopyToAsync(stream, ct);
        }

        var publicUrl = "/" + relDir.Replace('\\', '/') + "/" + fileName;
        var bytes = new FileInfo(absPath).Length;

        return new ImageStoreResult(true, Url: publicUrl, Bytes: bytes);
    }

    public Task DeleteAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("/uploads/", StringComparison.Ordinal))
            return Task.CompletedTask;

        var rel = url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var abs = Path.Combine(_env.WebRootPath, rel);

        var fullRoot = Path.GetFullPath(_env.WebRootPath);
        var fullPath = Path.GetFullPath(abs);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        if (File.Exists(fullPath))
        {
            try { File.Delete(fullPath); }
            catch (Exception ex) { _log.LogWarning(ex, "Failed to delete {Path}", fullPath); }
        }

        return Task.CompletedTask;
    }
}