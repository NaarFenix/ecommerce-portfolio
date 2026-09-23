namespace EcommercePortfolio.Domain.Entities;

public class Category
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public List<Product> Products { get; set; } = new();
}
