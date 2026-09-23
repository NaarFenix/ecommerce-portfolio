namespace EcommercePortfolio.Domain.Entities;

public class Customer
{
    public long Id { get; set; }
    public string Email { get; set; } = "";
    public string? PasswordHash { get; set; }
    public string FullName { get; set; } = "";
    public string? Phone { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
