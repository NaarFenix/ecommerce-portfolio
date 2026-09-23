using Microsoft.AspNetCore.Http;

namespace EcommercePortfolio.Infrastructure.Storage;

public record ImageStoreResult(
    bool Success,
    string? Url = null,
    string? Error = null,
    int? Width = null,
    int? Height = null,
    long? Bytes = null);

public interface IImageStorage
{
    /// <summary>Validates and stores an uploaded image for the given product.</summary>
    Task<ImageStoreResult> StoreAsync(long productId, IFormFile file, CancellationToken ct = default);

    /// <summary>Deletes a previously stored image by its public URL.</summary>
    Task DeleteAsync(string url, CancellationToken ct = default);
}