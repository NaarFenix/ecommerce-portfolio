using EcommercePortfolio.Data;
using EcommercePortfolio.Domain.Entities;
using System.Text.Json;

namespace EcommercePortfolio.Api.Auth;

public class AuditLogger
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;

    public AuditLogger(AppDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    public async Task LogAsync(
        long? adminUserId,
        string action,
        string? entityType = null,
        long? entityId = null,
        object? details = null)
    {
        var ip = _http.HttpContext?.Connection.RemoteIpAddress?.ToString();

        var log = new AuditLog
        {
            AdminUserId = adminUserId,
            Action      = action,
            EntityType  = entityType,
            EntityId    = entityId,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details),
            IpAddress   = ip,
            CreatedAt   = DateTimeOffset.UtcNow
        };

        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync();
    }
}