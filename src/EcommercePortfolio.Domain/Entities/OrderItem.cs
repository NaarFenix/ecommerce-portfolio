namespace EcommercePortfolio.Domain.Entities;

public class OrderItem
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public long? ProductId { get; set; }
    public Product? Product { get; set; }
    public string ProductNameSnapshot { get; set; } = "";
    public decimal UnitPriceSnapshot { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}
