namespace EcommercePortfolio.Domain.Entities;

public class Payment
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public string Provider { get; set; } = "stripe";
    public string ProviderPaymentId { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Status { get; set; } = "pending";
    public string? RawResponseJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
