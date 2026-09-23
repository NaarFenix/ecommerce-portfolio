using EcommercePortfolio.Api.Auth;
using EcommercePortfolio.Api.Html;
using EcommercePortfolio.Data;
using EcommercePortfolio.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace EcommercePortfolio.Api.Endpoints;

public static class CartEndpoints
{
    // ============================================================
    // FULL CART PAGE
    // ============================================================
    public static async Task<IResult> PageAsync(HttpContext ctx, AppDbContext db)
    {
        var cart = await CartIdentity.GetCartAsync(ctx, db);
        var content = RenderCartContent(cart);

        var html = $@"
<div class=""store-page-head"">
  <h1 class=""store-page-title"">Your cart</h1>
  <p class=""store-page-sub"">Review your items before checkout.</p>
</div>
<div id=""cart-content"">{content}</div>";

        return Render.StorePage(ctx, "/cart", html, "Cart — Atelier");
    }

    // ============================================================
    // CART FRAGMENT (HTMX swap target)
    // ============================================================
    public static async Task<IResult> FragmentAsync(HttpContext ctx, AppDbContext db)
    {
        var cart = await CartIdentity.GetCartAsync(ctx, db);
        var content = RenderCartContent(cart);
        return Results.Content($"<div id=\"cart-content\">{content}</div>", "text/html; charset=utf-8");
    }

    // ============================================================
    // HEADER BADGE (initial load + post-add swap)
    // ============================================================
    public static async Task<IResult> BadgeAsync(HttpContext ctx, AppDbContext db)
    {
        var cart = await CartIdentity.GetCartAsync(ctx, db);
        var count = cart?.Items.Sum(i => i.Quantity) ?? 0;
        return Results.Content(RenderBadge(count), "text/html; charset=utf-8");
    }

    // ============================================================
    // ADD ITEM
    // ============================================================
    public static async Task<IResult> AddAsync(HttpContext ctx, AppDbContext db)
    {
        var form = await ctx.Request.ReadFormAsync();
        if (!long.TryParse(form["productId"], out var productId))
            return Results.BadRequest("Invalid productId");

        var qty = int.TryParse(form["quantity"], out var q) ? q : 1;
        if (qty < 1) qty = 1;

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId && p.IsActive);
        if (product is null) return Results.NotFound();
        if (product.Stock == 0) return Results.BadRequest("Out of stock");

        var token = await CartIdentity.GetOrCreateTokenAsync(ctx, db);

        var cart = await db.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.SessionToken == token);

        if (cart is null)
        {
            cart = new Cart
            {
                SessionToken = token,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Carts.Add(cart);
            await db.SaveChangesAsync();
        }

        var existing = cart.Items.FirstOrDefault(i => i.ProductId == productId);
        var targetQty = (existing?.Quantity ?? 0) + qty;
        if (targetQty > product.Stock) targetQty = product.Stock;

        if (existing is null)
            db.CartItems.Add(new CartItem { CartId = cart.Id, ProductId = productId, Quantity = targetQty });
        else
            existing.Quantity = targetQty;

        cart.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var count = await db.CartItems
            .Where(i => i.CartId == cart.Id)
            .SumAsync(i => i.Quantity);

        return Results.Content(RenderBadge(count), "text/html; charset=utf-8");
    }

    // ============================================================
    // UPDATE QUANTITY (delta or absolute)
    // ============================================================
    public static async Task<IResult> UpdateQuantityAsync(HttpContext ctx, AppDbContext db, long id)
    {
        var cart = await CartIdentity.GetCartAsync(ctx, db);
        if (cart is null) return Results.NotFound();

        var item = cart.Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return Results.NotFound();

        var product = item.Product;
        if (product is null) return Results.NotFound();

        var form = await ctx.Request.ReadFormAsync();
        int newQty = item.Quantity;
        if (form.ContainsKey("delta") && int.TryParse(form["delta"], out var delta))
            newQty = item.Quantity + delta;
        else if (int.TryParse(form["quantity"], out var q))
            newQty = q;

        if (newQty > product.Stock) newQty = product.Stock;

        if (newQty < 1)
            db.CartItems.Remove(item);
        else
            item.Quantity = newQty;

        cart.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var refreshed = await CartIdentity.GetCartAsync(ctx, db);
        ctx.Response.Headers.Append("HX-Trigger", "cart-updated");
        return Results.Content($"<div id=\"cart-content\">{RenderCartContent(refreshed)}</div>",
            "text/html; charset=utf-8");
    }

    // ============================================================
    // REMOVE ITEM
    // ============================================================
    public static async Task<IResult> DeleteAsync(HttpContext ctx, AppDbContext db, long id)
    {
        var cart = await CartIdentity.GetCartAsync(ctx, db);
        if (cart is null) return Results.NotFound();

        var item = cart.Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return Results.NotFound();

        db.CartItems.Remove(item);
        cart.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var refreshed = await CartIdentity.GetCartAsync(ctx, db);
        ctx.Response.Headers.Append("HX-Trigger", "cart-updated");
        return Results.Content($"<div id=\"cart-content\">{RenderCartContent(refreshed)}</div>",
            "text/html; charset=utf-8");
    }

    // ============================================================
    // RENDER HELPERS
    // ============================================================
    public static string RenderBadge(int count) => count > 0
        ? $@"<span id=""cart-badge"" hx-get=""/cart/badge"" hx-trigger=""cart-updated from:body"" hx-swap=""outerHTML""><span class=""store-cart-count"">{count}</span></span>"
        : @"<span id=""cart-badge"" hx-get=""/cart/badge"" hx-trigger=""cart-updated from:body"" hx-swap=""outerHTML""></span>";

    public static string RenderCartContent(Cart? cart)
    {
        if (cart is null || cart.Items.Count == 0)
            return RenderEmptyCart();

        var sb = new StringBuilder();
        decimal subtotal = 0m;

        sb.Append("<div class=\"cart-layout\">");

        // ----- Items column -----
        sb.Append("<div class=\"cart-items\">");
        foreach (var item in cart.Items.OrderBy(i => i.Id))
        {
            var p = item.Product!;
            var lineTotal = p.Price * item.Quantity;
            subtotal += lineTotal;

            var primaryImage = p.Images
                .OrderByDescending(i => i.IsPrimary)
                .ThenBy(i => i.SortOrder)
                .ThenBy(i => i.Id)
                .FirstOrDefault();

            sb.Append("<div class=\"cart-item\">");

            // Thumb
            sb.Append("<a class=\"cart-item-thumb\" href=\"/products/")
              .Append(Layout.H(p.Slug)).Append("\">");
            if (primaryImage is not null)
                sb.Append("<img src=\"").Append(Layout.H(primaryImage.Url))
                  .Append("\" alt=\"").Append(Layout.H(p.Name)).Append("\">");
            else
                sb.Append("<span class=\"cart-item-thumb-initials\">").Append(Initials(p.Name)).Append("</span>");
            sb.Append("</a>");

            // Info
            sb.Append("<div class=\"cart-item-info\">");
            sb.Append("<a class=\"cart-item-name\" href=\"/products/")
              .Append(Layout.H(p.Slug)).Append("\">").Append(Layout.H(p.Name)).Append("</a>");
            sb.Append("<span class=\"cart-item-unit\">$").Append(p.Price.ToString("F2")).Append(" each</span>");
            sb.Append("</div>");

            // Qty stepper
            sb.Append("<div class=\"qty-stepper\">");
            sb.Append("<button type=\"button\" class=\"qty-btn\"")
              .Append(" hx-post=\"/cart/items/").Append(item.Id).Append("/quantity\"")
              .Append(" hx-vals='{\"delta\": -1}'")
              .Append(" hx-target=\"#cart-content\" hx-swap=\"outerHTML\"")
              .Append(" aria-label=\"Decrease quantity\">−</button>");
            sb.Append("<span class=\"qty-value\">").Append(item.Quantity).Append("</span>");
            sb.Append("<button type=\"button\" class=\"qty-btn\"")
              .Append(" hx-post=\"/cart/items/").Append(item.Id).Append("/quantity\"")
              .Append(" hx-vals='{\"delta\": 1}'")
              .Append(" hx-target=\"#cart-content\" hx-swap=\"outerHTML\"")
              .Append(" aria-label=\"Increase quantity\">+</button>");
            sb.Append("</div>");

            // Line total
            sb.Append("<div class=\"cart-item-total\">$").Append(lineTotal.ToString("F2")).Append("</div>");

            // Remove
            sb.Append("<button type=\"button\" class=\"cart-item-remove\"")
              .Append(" hx-post=\"/cart/items/").Append(item.Id).Append("/delete\"")
              .Append(" hx-target=\"#cart-content\" hx-swap=\"outerHTML\"")
              .Append(" aria-label=\"Remove item\">")
              .Append("<svg viewBox=\"0 0 24 24\" width=\"16\" height=\"16\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\">")
              .Append("<line x1=\"18\" y1=\"6\" x2=\"6\" y2=\"18\"/><line x1=\"6\" y1=\"6\" x2=\"18\" y2=\"18\"/>")
              .Append("</svg></button>");

            sb.Append("</div>");
        }
        sb.Append("</div>");

        // ----- Summary column -----
        sb.Append("<aside class=\"cart-summary\">");
        sb.Append("<h2 class=\"cart-summary-title\">Summary</h2>");
        sb.Append("<div class=\"cart-summary-row\"><span>Subtotal</span><span>$")
          .Append(subtotal.ToString("F2")).Append("</span></div>");
        sb.Append("<div class=\"cart-summary-row\"><span>Shipping</span><span class=\"cart-summary-muted\">Calculated at checkout</span></div>");
        sb.Append("<div class=\"cart-summary-divider\"></div>");
        sb.Append("<div class=\"cart-summary-row cart-summary-total\"><span>Total</span><span>$")
          .Append(subtotal.ToString("F2")).Append("</span></div>");
        sb.Append("<a href=\"/checkout\" class=\"cart-checkout-btn\">Proceed to checkout</a>");
        sb.Append("<a href=\"/products\" class=\"cart-continue\">← Continue shopping</a>");
        sb.Append("</aside>");

        sb.Append("</div>");
        return sb.ToString();
    }

    private static string RenderEmptyCart() => @"
<div class=""store-empty"" style=""padding:5rem 1.5rem;"">
  <div class=""store-empty-icon"">◌</div>
  <h3 class=""store-empty-title"">Your cart is empty</h3>
  <p class=""store-empty-sub"">Add a few pieces and they'll show up here.</p>
  <a href=""/products"" class=""store-btn store-btn-primary"" style=""margin-top:1.5rem;"">Browse the shop</a>
</div>";

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0][..1].ToUpperInvariant();
        return (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }
}