namespace EcommercePortfolio.Domain.Entities;

public class Product
{
    public long Id { get; set; }
    public long CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public decimal? CompareAtPrice { get; set; }
    public int Stock { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<ProductImage> Images { get; set; } = new();
}
