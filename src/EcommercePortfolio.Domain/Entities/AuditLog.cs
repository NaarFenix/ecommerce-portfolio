namespace EcommercePortfolio.Domain.Entities;

public class AuditLog
{
    public long Id { get; set; }
    public long? AdminUserId { get; set; }
    public AdminUser? AdminUser { get; set; }
    public string Action { get; set; } = "";
    public string? EntityType { get; set; }
    public long? EntityId { get; set; }
    public string? DetailsJson { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
