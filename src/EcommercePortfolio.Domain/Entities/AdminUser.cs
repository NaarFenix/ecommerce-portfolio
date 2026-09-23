namespace EcommercePortfolio.Domain.Entities;

public class AdminUser
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string? TotpSecret { get; set; }
    public bool Is2faEnabled { get; set; }
    public string Role { get; set; } = "Admin";
    public bool IsActive { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
