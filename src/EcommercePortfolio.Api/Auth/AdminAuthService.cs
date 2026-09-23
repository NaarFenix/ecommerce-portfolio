using EcommercePortfolio.Data;
using EcommercePortfolio.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EcommercePortfolio.Api.Auth;

public record LoginResult(bool Success, string? Reason = null);

public class AdminAuthService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly AppDbContext _db;
    private readonly PasswordHasher<AdminUser> _hasher = new();

    public AdminAuthService(AppDbContext db) => _db = db;

    public async Task<(LoginResult Result, AdminUser? User)> VerifyAsync(string username, string password)
    {
        var user = await _db.AdminUsers
            .FirstOrDefaultAsync(u => u.Username == username || u.Email == username);

        if (user is null)
            return (new LoginResult(false, "Invalid credentials."), null);

        if (!user.IsActive)
            return (new LoginResult(false, "Account disabled."), null);

        if (user.LockedUntil is { } locked && locked > DateTimeOffset.UtcNow)
            return (new LoginResult(false, "Account locked. Try again later."), user);

        var verify = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);

        if (verify == PasswordVerificationResult.Failed)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
                user.LockedUntil = DateTimeOffset.UtcNow.Add(LockoutDuration);

            await _db.SaveChangesAsync();
            return (new LoginResult(false, "Invalid credentials."), user);
        }

        // Success — reset counters
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastLoginAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        return (new LoginResult(true), user);
    }
}
