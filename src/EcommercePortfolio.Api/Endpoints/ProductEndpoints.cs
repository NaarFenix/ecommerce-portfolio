using EcommercePortfolio.Api.Auth;
using EcommercePortfolio.Api.Html;
using EcommercePortfolio.Data;
using EcommercePortfolio.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Claims;

namespace EcommercePortfolio.Api.Endpoints;

public static class ProductEndpoints
{
    private const int PageSize = 20;

    // ============================================================
    // LIST (with category filter + text search + pagination)
    // ============================================================
    public static async Task<IResult> ListAsync(HttpContext ctx, AppDbContext db,
        string? q, string? category, int page = 1)
    {
        if (page < 1) page = 1;
        long? categoryId = null;
        if (!string.IsNullOrWhiteSpace(category) && long.TryParse(category, out var parsed))
            categoryId = parsed;

        var query = db.Products
            .Include(p => p.Category)
            .AsQueryable();

        if (categoryId is { } cid)
            query = query.Where(p => p.CategoryId == cid);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(p => EF.Functions.ILike(p.Name, $"%{term}%")
                                  || EF.Functions.ILike(p.Sku,  $"%{term}%"));
        }

        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));

        var products = await query
            .OrderByDescending(p => p.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(p => new
            {
                p.Id, p.Sku, p.Name, p.Slug, p.Price, p.Stock, p.IsActive,
                CategoryName = p.Category.Name
            })
            .ToListAsync();

        var categories = await db.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync();

        var categoryOptions = "<option value=\"\">All categories</option>" +
            string.Concat(categories.Select(c =>
                $"<option value=\"{c.Id}\" {(categoryId == c.Id ? "selected" : "")}>{Layout.H(c.Name)}</option>"));

        var rows = products.Count == 0
            ? "<tr><td colspan=\"7\" class=\"muted\">No products found.</td></tr>"
            : string.Concat(products.Select(p =>
                "<tr>" +
                $"<td>{Layout.H(p.Sku)}</td>" +
                $"<td><a href=\"/admin/products/{p.Id}/edit\">{Layout.H(p.Name)}</a></td>" +
                $"<td>{Layout.H(p.CategoryName)}</td>" +
                $"<td>${p.Price:F2}</td>" +
                $"<td>{(p.Stock == 0 ? "<span class=\"badge badge-cancelled\">0</span>" : p.Stock.ToString())}</td>" +
                $"<td>{(p.IsActive
                    ? "<span class=\"badge badge-paid\">active</span>"
                    : "<span class=\"badge badge-cancelled\">inactive</span>")}</td>" +
                "<td class=\"actions\">" +
                  $"<a href=\"/admin/products/{p.Id}/edit\">Edit</a>" +
                  (p.IsActive
                    ? $"<form method=\"post\" action=\"/admin/products/{p.Id}/delete\" style=\"display:inline\" onsubmit=\"return confirm('Deactivate this product?');\"><button type=\"submit\" class=\"link danger\">Deactivate</button></form>"
                    : "") +
                "</td>" +
                "</tr>"));

        var pagination = totalPages > 1
            ? $"<div class=\"pagination\">Page {page} of {totalPages} · {total} products</div>"
            : $"<div class=\"pagination\">{total} product(s)</div>";

        var fragment = $@"
<div class=""filter-bar"">
  <input type=""search"" name=""q"" value=""{Layout.H(q)}"" placeholder=""Search name or SKU...""
         hx-get=""/admin/products"" hx-trigger=""input changed delay:300ms, search""
         hx-target=""#product-results"" hx-include=""[name='category']"" hx-indicator=""#search-spinner"">
  <select name=""category""
          hx-get=""/admin/products"" hx-trigger=""change""
          hx-target=""#product-results"" hx-include=""[name='q']"">
    {categoryOptions}
  </select>
  <span id=""search-spinner"" class=""htmx-indicator"">…</span>
</div>

<table class=""admin-table"">
  <thead><tr>
    <th>SKU</th><th>Name</th><th>Category</th><th>Price</th><th>Stock</th><th>Status</th><th></th>
  </tr></thead>
  <tbody>{rows}</tbody>
</table>
{pagination}";

        // If HTMX partial request → return just the fragment
        if (ctx.Request.Headers.ContainsKey("HX-Request"))
            return Results.Content($"<div id=\"product-results\">{fragment}</div>", "text/html; charset=utf-8");

        var fullHtml = $@"
<div class=""page-header"">
  <h2>Products</h2>
  <a href=""/admin/products/new"" class=""btn"">+ New product</a>
</div>
<div id=""product-results"">{fragment}</div>";

        return Render.AdminPage(ctx, "/admin/products", fullHtml, "Products");
    }

    // ============================================================
    // FORM (new / edit)
    // ============================================================
    public static async Task<IResult> FormAsync(HttpContext ctx, AppDbContext db, long? id,
        string? error = null)
    {
        Product? p = null;
        if (id is { } pid)
        {
            p = await db.Products.FirstOrDefaultAsync(x => x.Id == pid);
            if (p is null) return Results.NotFound();
        }

        var categories = await db.Categories
            .Where(c => c.IsActive || (p != null && c.Id == p.CategoryId))
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync();

        if (categories.Count == 0)
        {
            var noCats = "<h2>New product</h2>" +
                "<p class=\"notice\">You need at least one active category before creating a product. " +
                "<a href=\"/admin/categories/new\">Create a category</a> first.</p>";
            return Render.AdminPage(ctx, "/admin/products", noCats, "New product");
        }

        var catOptions = string.Concat(categories.Select(c =>
            $"<option value=\"{c.Id}\" {(p?.CategoryId == c.Id ? "selected" : "")}>{Layout.H(c.Name)}</option>"));

        var action = p is null ? "/admin/products" : $"/admin/products/{p.Id}";
        var title = p is null ? "New product" : $"Edit product #{p.Id}";

        var errorBlock = string.IsNullOrWhiteSpace(error)
            ? ""
            : $"<p class=\"error\">{Layout.H(error)}</p>";

        var html = $@"
<h2>{Layout.H(title)}</h2>
{errorBlock}
<form method=""post"" action=""{action}"">
  <div>
    <label for=""categoryId"">Category</label>
    <select id=""categoryId"" name=""categoryId"" required>{catOptions}</select>
  </div>
  <div>
    <label for=""sku"">SKU (unique)</label>
    <input id=""sku"" name=""sku"" type=""text"" required maxlength=""64"" value=""{Layout.H(p?.Sku)}"">
  </div>
  <div>
    <label for=""name"">Name</label>
    <input id=""name"" name=""name"" type=""text"" required maxlength=""200"" value=""{Layout.H(p?.Name)}"">
  </div>
  <div>
    <label for=""slug"">Slug (unique, lowercase-with-dashes)</label>
    <input id=""slug"" name=""slug"" type=""text"" required maxlength=""220"" pattern=""[a-z0-9]+(-[a-z0-9]+)*"" value=""{Layout.H(p?.Slug)}"">
  </div>
  <div>
    <label for=""description"">Description</label>
    <textarea id=""description"" name=""description"" rows=""5"">{Layout.H(p?.Description)}</textarea>
  </div>
  <div>
    <label for=""price"">Price (USD)</label>
    <input id=""price"" name=""price"" type=""number"" step=""0.01"" min=""0"" required value=""{(p?.Price.ToString("F2", CultureInfo.InvariantCulture) ?? "")}"">
  </div>
  <div>
    <label for=""compareAtPrice"">Compare-at price (optional — original price for ""sale"" badge)</label>
    <input id=""compareAtPrice"" name=""compareAtPrice"" type=""number"" step=""0.01"" min=""0"" value=""{(p?.CompareAtPrice?.ToString("F2", CultureInfo.InvariantCulture) ?? "")}"">
  </div>
  <div>
    <label for=""stock"">Stock</label>
    <input id=""stock"" name=""stock"" type=""number"" min=""0"" required value=""{p?.Stock ?? 0}"">
  </div>
  <div>
    <label><input type=""checkbox"" name=""isActive"" value=""true"" {(p?.IsActive ?? true ? "checked" : "")}> Active</label>
  </div>
  <div class=""form-actions"">
    <button type=""submit"">Save</button>
    <a href=""/admin/products"" class=""btn-secondary"">Cancel</a>
    {(p is null ? "" : $"<a href=\"/admin/products/{p.Id}/images\" class=\"btn-secondary\">Manage images</a>")}
  </div>
</form>";

        return Render.AdminPage(ctx, "/admin/products", html, title);
    }

    // ============================================================
    // CREATE
    // ============================================================
    public static async Task<IResult> CreateAsync(HttpContext ctx, AppDbContext db, AuditLogger audit)
    {
        var form = await ctx.Request.ReadFormAsync();
        var parsed = ParseForm(form, out var error);
        if (error is not null) return await ReRenderForm(ctx, db, null, error);

        if (await db.Products.AnyAsync(p => p.Sku == parsed.Sku))
            return await ReRenderForm(ctx, db, null, $"SKU '{parsed.Sku}' is already in use.");
        if (await db.Products.AnyAsync(p => p.Slug == parsed.Slug))
            return await ReRenderForm(ctx, db, null, $"Slug '{parsed.Slug}' is already in use.");
        if (!await db.Categories.AnyAsync(c => c.Id == parsed.CategoryId))
            return await ReRenderForm(ctx, db, null, "Selected category does not exist.");

        var now = DateTimeOffset.UtcNow;
        var product = new Product
        {
            CategoryId = parsed.CategoryId,
            Sku = parsed.Sku,
            Name = parsed.Name,
            Slug = parsed.Slug,
            Description = parsed.Description,
            Price = parsed.Price,
            CompareAtPrice = parsed.CompareAtPrice,
            Stock = parsed.Stock,
            IsActive = parsed.IsActive,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        await audit.LogAsync(GetAdminId(ctx), "product.create", "product", product.Id,
            new { sku = product.Sku, name = product.Name, price = product.Price });

        return Results.Redirect("/admin/products");
    }

    // ============================================================
    // UPDATE
    // ============================================================
    public static async Task<IResult> UpdateAsync(HttpContext ctx, AppDbContext db, AuditLogger audit, long id)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return Results.NotFound();

        var form = await ctx.Request.ReadFormAsync();
        var parsed = ParseForm(form, out var error);
        if (error is not null) return await ReRenderForm(ctx, db, id, error);

        if (await db.Products.AnyAsync(p => p.Sku == parsed.Sku && p.Id != id))
            return await ReRenderForm(ctx, db, id, $"SKU '{parsed.Sku}' is already in use.");
        if (await db.Products.AnyAsync(p => p.Slug == parsed.Slug && p.Id != id))
            return await ReRenderForm(ctx, db, id, $"Slug '{parsed.Slug}' is already in use.");

        product.CategoryId = parsed.CategoryId;
        product.Sku = parsed.Sku;
        product.Name = parsed.Name;
        product.Slug = parsed.Slug;
        product.Description = parsed.Description;
        product.Price = parsed.Price;
        product.CompareAtPrice = parsed.CompareAtPrice;
        product.Stock = parsed.Stock;
        product.IsActive = parsed.IsActive;
        product.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        await audit.LogAsync(GetAdminId(ctx), "product.update", "product", product.Id,
            new { sku = product.Sku, name = product.Name, price = product.Price, stock = product.Stock });

        return Results.Redirect("/admin/products");
    }

    // ============================================================
    // SOFT-DELETE (deactivate)
    // ============================================================
    public static async Task<IResult> DeleteAsync(HttpContext ctx, AppDbContext db, AuditLogger audit, long id)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return Results.NotFound();

        product.IsActive = false;
        product.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        await audit.LogAsync(GetAdminId(ctx), "product.deactivate", "product", product.Id,
            new { sku = product.Sku, slug = product.Slug });

        return Results.Redirect("/admin/products");
    }

    // ============================================================
    // HELPERS
    // ============================================================
    private sealed record ParsedForm(
        long CategoryId, string Sku, string Name, string Slug, string Description,
        decimal Price, decimal? CompareAtPrice, int Stock, bool IsActive);

    private static ParsedForm ParseForm(IFormCollection form, out string? error)
    {
        error = null;

        if (!long.TryParse(form["categoryId"], out var categoryId))
        {
            error = "Please select a category.";
            return new ParsedForm(0, "", "", "", "", 0m, null, 0, true);
        }

        var sku = form["sku"].ToString().Trim();
        var name = form["name"].ToString().Trim();
        var slug = form["slug"].ToString().Trim().ToLowerInvariant();
        var description = form["description"].ToString().Trim();

        if (string.IsNullOrWhiteSpace(sku)) { error = "SKU is required."; return new ParsedForm(categoryId, sku, name, slug, description, 0m, null, 0, true); }
        if (string.IsNullOrWhiteSpace(name)) { error = "Name is required."; return new ParsedForm(categoryId, sku, name, slug, description, 0m, null, 0, true); }
        if (string.IsNullOrWhiteSpace(slug)) { error = "Slug is required."; return new ParsedForm(categoryId, sku, name, slug, description, 0m, null, 0, true); }

        if (!decimal.TryParse(form["price"], NumberStyles.Number, CultureInfo.InvariantCulture, out var price) || price < 0)
        {
            error = "Price must be a non-negative number.";
            return new ParsedForm(categoryId, sku, name, slug, description, 0m, null, 0, true);
        }

        decimal? compareAt = null;
        var compareRaw = form["compareAtPrice"].ToString();
        if (!string.IsNullOrWhiteSpace(compareRaw))
        {
            if (!decimal.TryParse(compareRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var cmp) || cmp < 0)
            {
                error = "Compare-at price must be a non-negative number or left blank.";
                return new ParsedForm(categoryId, sku, name, slug, description, price, null, 0, true);
            }
            compareAt = cmp;
        }

        if (!int.TryParse(form["stock"], out var stock) || stock < 0)
        {
            error = "Stock must be a non-negative integer.";
            return new ParsedForm(categoryId, sku, name, slug, description, price, compareAt, 0, true);
        }

        var isActive = form["isActive"].ToString() == "true";

        return new ParsedForm(categoryId, sku, name, slug, description, price, compareAt, stock, isActive);
    }

    private static async Task<IResult> ReRenderForm(HttpContext ctx, AppDbContext db, long? id, string error)
        => await FormAsync(ctx, db, id, error);

    private static long? GetAdminId(HttpContext ctx)
    {
        var idStr = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(idStr, out var parsed) ? parsed : null;
    }
}