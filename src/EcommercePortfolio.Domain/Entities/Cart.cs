namespace EcommercePortfolio.Domain.Entities;

public class Cart
{
    public long Id { get; set; }
    public long? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid SessionToken { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<CartItem> Items { get; set; } = new();
}
