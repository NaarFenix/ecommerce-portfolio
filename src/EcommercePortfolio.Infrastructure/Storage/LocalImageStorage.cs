using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System.IO;

namespace EcommercePortfolio.Infrastructure.Storage;

public class LocalImageStorage : IImageStorage
{
    private const long MaxBytes     = 5 * 1024 * 1024;
    private const int  MinDimension = 800;
    private const int  MaxDimension = 4000;
    private const int  ResizeTo     = 2000;
    private const int  JpegQuality  = 85;

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

        // Decode into memory
        using var input = file.OpenReadStream();
        using var memory = new MemoryStream();
        await input.CopyToAsync(memory, ct);
        memory.Position = 0;

        using var original = SKBitmap.Decode(memory);
        if (original is null)
            return new ImageStoreResult(false, Error: "The file is not a valid image.");

        if (original.Width < MinDimension || original.Height < MinDimension)
            return new ImageStoreResult(false,
                Error: $"Image must be at least {MinDimension}×{MinDimension} px (got {original.Width}×{original.Height}).");

        if (original.Width > MaxDimension || original.Height > MaxDimension)
            return new ImageStoreResult(false,
                Error: $"Image must be at most {MaxDimension}×{MaxDimension} px (got {original.Width}×{original.Height}).");

        // Compute resize target (longest edge → ResizeTo)
        int targetW = original.Width;
        int targetH = original.Height;
        if (targetW > ResizeTo || targetH > ResizeTo)
        {
            var scale = (double)ResizeTo / Math.Max(targetW, targetH);
            targetW = Math.Max(1, (int)Math.Round(targetW * scale));
            targetH = Math.Max(1, (int)Math.Round(targetH * scale));
        }

        using var resized = original.Resize(new SKImageInfo(targetW, targetH), new SKSamplingOptions(SKCubicResampler.Mitchell));
        if (resized is null)
            return new ImageStoreResult(false, Error: "Failed to resize image.");

        // Save as JPEG
        var relDir = Path.Combine("uploads", "products", productId.ToString());
        var absDir = Path.Combine(_env.WebRootPath, relDir);
        Directory.CreateDirectory(absDir);

        var fileName = $"{Guid.NewGuid():N}.jpg";
        var absPath = Path.Combine(absDir, fileName);

        await using (var outStream = File.Create(absPath))
        using (var data = resized.Encode(SKEncodedImageFormat.Jpeg, JpegQuality))
        {
            data.SaveTo(outStream);
        }

        var publicUrl = "/" + relDir.Replace('\\', '/') + "/" + fileName;
        var bytes = new FileInfo(absPath).Length;

        return new ImageStoreResult(true, Url: publicUrl,
            Width: targetW, Height: targetH, Bytes: bytes);
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