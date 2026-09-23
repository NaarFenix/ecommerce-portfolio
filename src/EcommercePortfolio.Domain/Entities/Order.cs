namespace EcommercePortfolio.Domain.Entities;

public class Order
{
    public long Id { get; set; }
    public string OrderNumber { get; set; } = "";
    public long? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string Email { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Phone { get; set; }
    public string ShippingAddress { get; set; } = "";
    public string City { get; set; } = "";
    public string? PostalCode { get; set; }
    public string Country { get; set; } = "";
    public decimal Subtotal { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = "pending";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<OrderItem> Items { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
}
