using EcommercePortfolio.Data;
using EcommercePortfolio.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EcommercePortfolio.Api.Auth;

public static class AdminSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration config, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (await db.AdminUsers.AnyAsync())
            return;

        var username = config["SeedAdmin:Username"] ?? "admin";
        var email    = config["SeedAdmin:Email"]    ?? "admin@example.com";
        var password = config["SeedAdmin:Password"];

        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("No SeedAdmin:Password configured — skipping admin seed.");
            return;
        }

        var hasher = new PasswordHasher<AdminUser>();
        var admin = new AdminUser
        {
            Username     = username,
            Email        = email,
            Role         = "SuperAdmin",
            IsActive     = true,
            CreatedAt    = DateTimeOffset.UtcNow
        };

        admin.PasswordHash = hasher.HashPassword(admin, password);

        db.AdminUsers.Add(admin);
        await db.SaveChangesAsync();

        logger.LogInformation("Seeded initial admin user '{Username}'.", username);
    }
}
