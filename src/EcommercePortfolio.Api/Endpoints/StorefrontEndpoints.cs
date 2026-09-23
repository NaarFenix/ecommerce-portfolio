using EcommercePortfolio.Api.Html;
using EcommercePortfolio.Data;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace EcommercePortfolio.Api.Endpoints;

public static class StorefrontEndpoints
{
    // ============================================================
    // HOME
    // ============================================================
    public static async Task<IResult> HomeAsync(HttpContext ctx, AppDbContext db)
    {
        // Featured = latest 8 active products with at least one image
        var products = await db.Products
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.Id)
            .Take(8)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Slug,
                p.Price,
                p.CompareAtPrice,
                PrimaryImageUrl = p.Images
                    .OrderByDescending(i => i.IsPrimary)
                    .ThenBy(i => i.SortOrder)
                    .Select(i => i.Url)
                    .FirstOrDefault()
            })
            .ToListAsync();

        var gridHtml = products.Count == 0
            ? RenderEmptyState("Nothing here yet.", "Products will appear here once they're added.")
            : RenderProductGrid(products.Select(p => new ProductCardData(
                p.Id, p.Name, p.Slug, p.Price, p.CompareAtPrice, p.PrimaryImageUrl)).ToList());

        // Category tiles
        var catRows = await db.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new CategoryTileData(
                c.Slug, c.Name,
                c.Products.Count(p => p.IsActive)))
            .ToListAsync();

        var tilesHtml = RenderCategoryTiles(catRows);

        var html = $@"
<section class=""store-hero"">
  <div class=""store-hero-inner"">
    <p class=""store-hero-eyebrow"">Curated Goods · Portfolio Demo</p>
    <h1 class=""store-hero-title"">Objects worth keeping.</h1>
    <p class=""store-hero-sub"">A small shop, built to last. Selected pieces, honest prices, no noise.</p>
    <div class=""store-hero-actions"">
      <a href=""/products"" class=""store-btn store-btn-primary"">Browse the shop</a>
      <a href=""/search"" class=""store-btn store-btn-ghost"">Search</a>
    </div>
  </div>
</section>

{(tilesHtml.Length == 0 ? "" : $@"<section class=""store-section"">
  <div class=""store-section-head"">
    <h2 class=""store-section-title"">Browse by category</h2>
  </div>
  {tilesHtml}
</section>")}

<section class=""store-section"">
  <div class=""store-section-head"">
    <h2 class=""store-section-title"">Featured</h2>
    <a href=""/products"" class=""store-link"">See all →</a>
  </div>
  {gridHtml}
</section>";

        return Render.StorePage(ctx, "/", html, "Atelier — Curated Goods");
    }

    // ============================================================
    // CATALOG — /products?category=slug&page=N
    // ============================================================
    private const int PageSize = 9;

    public static async Task<IResult> CatalogAsync(
        HttpContext ctx, AppDbContext db,
        string? category, int page = 1)
    {
        if (page < 1) page = 1;

        var query = db.Products.Where(p => p.IsActive);

        string? activeCategoryName = null;
        if (!string.IsNullOrWhiteSpace(category))
        {
            var cat = await db.Categories
                .Where(c => c.Slug == category && c.IsActive)
                .Select(c => new { c.Id, c.Name })
                .FirstOrDefaultAsync();

            if (cat is not null)
            {
                query = query.Where(p => p.CategoryId == cat.Id);
                activeCategoryName = cat.Name;
            }
        }

        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));

        var products = await query
            .OrderByDescending(p => p.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Slug,
                p.Price,
                p.CompareAtPrice,
                PrimaryImageUrl = p.Images
                    .OrderByDescending(i => i.IsPrimary)
                    .ThenBy(i => i.SortOrder)
                    .Select(i => i.Url)
                    .FirstOrDefault()
            })
            .ToListAsync();

        var categories = await db.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new { c.Slug, c.Name })
            .ToListAsync();

        // Category tabs
        var tabs = new StringBuilder();
        tabs.Append("<nav class=\"cat-tabs\">");
        tabs.Append("<a href=\"/products\" class=\"cat-tab")
            .Append(string.IsNullOrEmpty(category) ? " active" : "")
            .Append("\">All</a>");

        foreach (var c in categories)
        {
            var isActive = c.Slug == category;
            tabs.Append("<a href=\"/products?category=").Append(Layout.H(c.Slug))
                .Append("\" class=\"cat-tab").Append(isActive ? " active" : "")
                .Append("\">").Append(Layout.H(c.Name)).Append("</a>");
        }
        tabs.Append("</nav>");

        var gridHtml = products.Count == 0
            ? RenderEmptyState("No products here.", "Try a different category or check back later.")
            : RenderProductGrid(products.Select(p => new ProductCardData(
                p.Id, p.Name, p.Slug, p.Price, p.CompareAtPrice, p.PrimaryImageUrl)).ToList());

        // Pagination
        var pager = new StringBuilder();
        if (totalPages > 1)
        {
            pager.Append("<nav class=\"store-pager\">");
            var catQs = string.IsNullOrEmpty(category) ? "" : $"category={Layout.H(category)}&";

            if (page > 1)
                pager.Append("<a href=\"/products?").Append(catQs).Append("page=").Append(page - 1)
                     .Append("\" class=\"store-pager-link\">← Previous</a>");
            else
                pager.Append("<span class=\"store-pager-link disabled\">← Previous</span>");

            pager.Append("<span class=\"store-pager-info\">Page ").Append(page)
                 .Append(" of ").Append(totalPages).Append(" · ").Append(total).Append(" item(s)</span>");

            if (page < totalPages)
                pager.Append("<a href=\"/products?").Append(catQs).Append("page=").Append(page + 1)
                     .Append("\" class=\"store-pager-link\">Next →</a>");
            else
                pager.Append("<span class=\"store-pager-link disabled\">Next →</span>");

            pager.Append("</nav>");
        }
        else
        {
            pager.Append("<div class=\"store-pager-info-solo\">").Append(total).Append(" item(s)</div>");
        }

        var heading = activeCategoryName is null ? "The Shop" : activeCategoryName;
        var sub = activeCategoryName is null
            ? "Everything currently in the collection."
            : $"Pieces in {activeCategoryName}.";

        var html = $@"<div class=""store-page-head"">
  <h1 class=""store-page-title"">{Layout.H(heading)}</h1>
  <p class=""store-page-sub"">{Layout.H(sub)}</p>
</div>
{tabs}
{gridHtml}
{pager}";

        return Render.StorePage(ctx, "/products", html, $"{heading} — Atelier");
    }

    // ============================================================
    // DETAIL — /products/{slug}
    // ============================================================
    public static async Task<IResult> DetailAsync(HttpContext ctx, AppDbContext db, string slug)
    {
        var product = await db.Products
            .Where(p => p.Slug == slug && p.IsActive)
            .Select(p => new
            {
                p.Id, p.Name, p.Slug, p.Description,
                p.Price, p.CompareAtPrice, p.Stock,
                CategorySlug = p.Category.Slug,
                CategoryName = p.Category.Name,
                Images = p.Images
                    .OrderByDescending(i => i.IsPrimary)
                    .ThenBy(i => i.SortOrder)
                    .ThenBy(i => i.Id)
                    .Select(i => new { i.Url, i.AltText })
                    .ToList()
            })
            .FirstOrDefaultAsync();

        if (product is null)
            return Render.StorePage(ctx, "/products",
                RenderEmptyState("Product not found.", "It may have been removed or renamed."),
                "Not found — Atelier");

        var isOnSale = product.CompareAtPrice is { } cmp && cmp > product.Price;

        // ---------- Gallery ----------
        var gallery = new StringBuilder();
        if (product.Images.Count == 0)
        {
            gallery.Append("<div class=\"pdp-gallery-main pdp-gallery-placeholder\">")
                   .Append(Layout.H(Initials(product.Name)))
                   .Append("</div>");
        }
        else
        {
            gallery.Append("<div class=\"pdp-gallery\" id=\"pdp-gallery\">");
            gallery.Append("<div class=\"pdp-gallery-main\">");
            gallery.Append("<img id=\"pdp-main-img\" src=\"").Append(Layout.H(product.Images[0].Url))
                   .Append("\" alt=\"").Append(Layout.H(product.Images[0].AltText))
                   .Append("\">");
            gallery.Append("</div>");

            if (product.Images.Count > 1)
            {
                gallery.Append("<div class=\"pdp-gallery-thumbs\">");
                foreach (var img in product.Images)
                {
                    gallery.Append("<button type=\"button\" class=\"pdp-thumb\" data-src=\"")
                           .Append(Layout.H(img.Url)).Append("\">")
                           .Append("<img src=\"").Append(Layout.H(img.Url))
                           .Append("\" alt=\"").Append(Layout.H(img.AltText)).Append("\">")
                           .Append("</button>");
                }
                gallery.Append("</div>");
            }
            gallery.Append("</div>");
        }

        // ---------- Price block ----------
        var priceHtml = new StringBuilder();
        priceHtml.Append("<div class=\"pdp-price\">");
        priceHtml.Append("<span class=\"pdp-price-now\">$").Append(product.Price.ToString("F2")).Append("</span>");
        if (isOnSale)
            priceHtml.Append("<span class=\"pdp-price-was\">$")
                     .Append(product.CompareAtPrice!.Value.ToString("F2"))
                     .Append("</span>");
        priceHtml.Append("</div>");

        // ---------- Stock ----------
        var stockHtml = product.Stock == 0
            ? "<span class=\"pdp-stock pdp-stock-out\">Out of stock</span>"
            : product.Stock < 5
                ? $"<span class=\"pdp-stock pdp-stock-low\">Only {product.Stock} left</span>"
                : $"<span class=\"pdp-stock pdp-stock-ok\">In stock</span>";

        // ---------- Add to cart (button is placeholder until Phase 5) ----------
        var atcDisabled = product.Stock == 0;
        var atcBtn = atcDisabled
            ? "<button class=\"pdp-atc\" disabled>Out of stock</button>"
            : $@"<button class=""pdp-atc""
                        hx-post=""/cart/items""
                        hx-vals='{{""productId"": {product.Id}, ""quantity"": 1}}'
                        hx-target=""#cart-badge""
                        hx-swap=""outerHTML"">
                  Add to cart
                </button>";

        var descriptionHtml = string.IsNullOrWhiteSpace(product.Description)
            ? ""
            : $"<div class=\"pdp-description\">{Layout.H(product.Description)}</div>";

        var breadcrumb = $@"<nav class=""pdp-breadcrumb"">
  <a href=""/products"">Shop</a>
  <span class=""pdp-breadcrumb-sep"">/</span>
  <a href=""/products?category={Layout.H(product.CategorySlug)}"">{Layout.H(product.CategoryName)}</a>
</nav>";

        var html = $@"{breadcrumb}
<div class=""pdp"">
  <div class=""pdp-media"">{gallery}</div>
  <div class=""pdp-info"">
    <h1 class=""pdp-name"">{Layout.H(product.Name)}</h1>
    {priceHtml}
    {stockHtml}
    {descriptionHtml}
    <div class=""pdp-actions"">{atcBtn}</div>
    <p class=""pdp-note"">Free returns · Ships within 2 days</p>
  </div>
</div>
<script>
(function () {{
  var gallery = document.getElementById('pdp-gallery');
  if (!gallery) return;
  var main = document.getElementById('pdp-main-img');
  gallery.addEventListener('click', function (e) {{
    var btn = e.target.closest('.pdp-thumb');
    if (!btn) return;
    var src = btn.getAttribute('data-src');
    if (src && main) main.src = src;
    gallery.querySelectorAll('.pdp-thumb').forEach(function (t) {{ t.classList.remove('active'); }});
    btn.classList.add('active');
  }});
}})();
</script>";

        return Render.StorePage(ctx, "/products", html, $"{product.Name} — Atelier");
    }

    // ============================================================
    // SEARCH — /search?q=term
    // ============================================================
    public static async Task<IResult> SearchAsync(HttpContext ctx, AppDbContext db, string? q)
    {
        var term = q?.Trim() ?? "";
        List<ProductCardData> results = new();

        if (!string.IsNullOrWhiteSpace(term))
        {
            results = await db.Products
                .FromSqlInterpolated($@"
                    SELECT * FROM products
                    WHERE is_active = TRUE
                      AND to_tsvector('english', name || ' ' || description)
                          @@ plainto_tsquery('english', {term})
                    ORDER BY ts_rank(
                        to_tsvector('english', name || ' ' || description),
                        plainto_tsquery('english', {term})
                    ) DESC
                    LIMIT 30")
                .Select(p => new ProductCardData(
                    p.Id, p.Name, p.Slug, p.Price, p.CompareAtPrice,
                    p.Images.OrderByDescending(i => i.IsPrimary)
                            .ThenBy(i => i.SortOrder)
                            .Select(i => i.Url)
                            .FirstOrDefault()))
                .ToListAsync();
        }

        var countLine = string.IsNullOrWhiteSpace(term)
            ? "What are you looking for?"
            : $"{results.Count} result(s) for <b>{Layout.H(term)}</b>";

        var resultsHtml = results.Count == 0
            ? (string.IsNullOrWhiteSpace(term)
                ? RenderEmptyState("Search the collection.", "Type something to begin.")
                : RenderEmptyState($"No results for \"{term}\".", "Try a different word or browse the shop."))
            : RenderProductGrid(results);

        var inner = $@"<p class=""search-count"">{countLine}</p>{resultsHtml}";

        // HTMX fragment request → return ONLY the inner results, nothing else.
        if (ctx.Request.Headers.ContainsKey("HX-Request"))
            return Results.Content(inner, "text/html; charset=utf-8");

        // Full page load
        var html = $@"<div class=""store-page-head"">
  <h1 class=""store-page-title"">Search</h1>
</div>
<div class=""search-bar-wrap"">
  <input type=""search"" name=""q"" value=""{Layout.H(term)}""
         placeholder=""Search products...""
         hx-get=""/search"" hx-trigger=""input changed delay:300ms, search""
         hx-target=""#search-results"" hx-indicator=""#search-spinner""
         autofocus>
  <span id=""search-spinner"" class=""htmx-indicator search-spinner"">…</span>
</div>
<div id=""search-results"">{inner}</div>";

        return Render.StorePage(ctx, "/search", html, "Search — Atelier");
    }

    // ============================================================
    // SHARED RENDER HELPERS (reused in later sub-phases)
    // ============================================================

    public record ProductCardData(
        long Id, string Name, string Slug, decimal Price, decimal? CompareAtPrice, string? ImageUrl);

    public static string RenderProductGrid(List<ProductCardData> products)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"product-grid\">");
        foreach (var p in products)
            sb.Append(RenderProductCard(p));
        sb.Append("</div>");
        return sb.ToString();
    }

    public static string RenderProductCard(ProductCardData p)
    {
        var sb = new StringBuilder();
        var hasImage = !string.IsNullOrEmpty(p.ImageUrl);
        var isOnSale = p.CompareAtPrice is { } cmp && cmp > p.Price;

        sb.Append("<a class=\"product-card\" href=\"/products/").Append(Layout.H(p.Slug)).Append("\">");
        sb.Append("<div class=\"product-card-image\">");
        if (hasImage)
        {
            sb.Append("<img src=\"").Append(Layout.H(p.ImageUrl))
              .Append("\" alt=\"").Append(Layout.H(p.Name))
              .Append("\" loading=\"lazy\">");
        }
        else
        {
            sb.Append("<div class=\"product-card-image-placeholder\">")
              .Append(Layout.H(Initials(p.Name)))
              .Append("</div>");
        }
        if (isOnSale)
            sb.Append("<span class=\"product-card-badge\">Sale</span>");
        sb.Append("</div>");

        sb.Append("<div class=\"product-card-body\">");
        sb.Append("<h3 class=\"product-card-name\">").Append(Layout.H(p.Name)).Append("</h3>");

        sb.Append("<div class=\"product-card-price\">");
        sb.Append("<span class=\"price-now\">$").Append(p.Price.ToString("F2")).Append("</span>");
        if (isOnSale)
        {
            sb.Append("<span class=\"price-was\">$")
              .Append(p.CompareAtPrice!.Value.ToString("F2"))
              .Append("</span>");
        }
        sb.Append("</div>");
        sb.Append("</div>");
        sb.Append("</a>");
        return sb.ToString();
    }

    public static string RenderEmptyState(string title, string? subtitle = null)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"store-empty\">");
        sb.Append("<div class=\"store-empty-icon\" aria-hidden=\"true\">◌</div>");
        sb.Append("<h3 class=\"store-empty-title\">").Append(Layout.H(title)).Append("</h3>");
        if (!string.IsNullOrWhiteSpace(subtitle))
            sb.Append("<p class=\"store-empty-sub\">").Append(Layout.H(subtitle)).Append("</p>");
        sb.Append("</div>");
        return sb.ToString();
    }

    public record CategoryTileData(string Slug, string Name, int ProductCount);

    public static string RenderCategoryTiles(List<CategoryTileData> categories)
    {
        if (categories.Count == 0) return "";

        var sb = new StringBuilder();
        sb.Append("<div class=\"category-tiles\">");
        foreach (var c in categories)
        {
            sb.Append("<a href=\"/products?category=").Append(Layout.H(c.Slug))
              .Append("\" class=\"category-tile\">");
            sb.Append("<span class=\"category-tile-name\">").Append(Layout.H(c.Name)).Append("</span>");
            sb.Append("<span class=\"category-tile-count\">")
              .Append(c.ProductCount).Append(" item").Append(c.ProductCount == 1 ? "" : "s")
              .Append("</span>");
            sb.Append("<span class=\"category-tile-arrow\">→</span>");
            sb.Append("</a>");
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0][..1].ToUpperInvariant();
        return (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }
}