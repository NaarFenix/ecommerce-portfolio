using EcommercePortfolio.Data;
using EcommercePortfolio.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EcommercePortfolio.Api.Auth;

public static class CartIdentity
{
    public const string CookieName = "cart_token";

    public static Guid? TryGetToken(HttpContext ctx)
    {
        if (ctx.Request.Cookies.TryGetValue(CookieName, out var raw)
            && Guid.TryParse(raw, out var g))
            return g;
        return null;
    }

    public static async Task<Guid> GetOrCreateTokenAsync(HttpContext ctx, AppDbContext db)
    {
        var existing = TryGetToken(ctx);
        if (existing is { } g)
        {
            var exists = await db.Carts.AnyAsync(c => c.SessionToken == g);
            if (exists) return g;
        }

        var token = Guid.NewGuid();
        ctx.Response.Cookies.Append(CookieName, token.ToString(), new CookieOptions
        {
            HttpOnly = true,
            Secure   = ctx.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires  = DateTimeOffset.UtcNow.AddDays(30),
            Path     = "/"
        });
        return token;
    }

    public static async Task<Cart?> GetCartAsync(HttpContext ctx, AppDbContext db)
    {
        var token = TryGetToken(ctx);
        if (token is null) return null;

        return await db.Carts
            .Include(c => c.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p.Images)
            .FirstOrDefaultAsync(c => c.SessionToken == token.Value);
    }
}