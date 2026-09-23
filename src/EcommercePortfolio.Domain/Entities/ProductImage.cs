namespace EcommercePortfolio.Domain.Entities;

public class ProductImage
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string Url { get; set; } = "";
    public string AltText { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }
}
