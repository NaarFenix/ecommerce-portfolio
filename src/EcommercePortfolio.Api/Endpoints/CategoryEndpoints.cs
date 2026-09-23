using EcommercePortfolio.Api.Auth;
using EcommercePortfolio.Api.Html;
using EcommercePortfolio.Data;
using EcommercePortfolio.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EcommercePortfolio.Api.Endpoints;

public static class CategoryEndpoints
{
    // -------- List --------
    public static async Task<IResult> ListAsync(HttpContext ctx, AppDbContext db)
    {
        var categories = await db.Categories
            .OrderBy(c => c.Id)
            .Select(c => new
            {
                c.Id, c.Name, c.Slug, c.IsActive, c.CreatedAt,
                ProductCount = c.Products.Count
            })
            .ToListAsync();

        var rows = categories.Count == 0
            ? "<tr><td colspan=\"6\" class=\"muted\">No categories yet.</td></tr>"
            : string.Concat(categories.Select(c =>
                "<tr>" +
                $"<td>{c.Id}</td>" +
                $"<td>{Layout.H(c.Name)}</td>" +
                $"<td>{Layout.H(c.Slug)}</td>" +
                $"<td>{c.ProductCount}</td>" +
                $"<td>{(c.IsActive
                    ? "<span class=\"badge badge-paid\">active</span>"
                    : "<span class=\"badge badge-cancelled\">inactive</span>")}</td>" +
                "<td class=\"actions\">" +
                  $"<a href=\"/admin/categories/{c.Id}/edit\">Edit</a>" +
                  (c.IsActive
                    ? $"<form method=\"post\" action=\"/admin/categories/{c.Id}/delete\" style=\"display:inline\" onsubmit=\"return confirm('Deactivate this category?');\"><button type=\"submit\" class=\"link danger\">Deactivate</button></form>"
                    : "") +
                "</td>" +
                "</tr>"));

        var html = $@"
<div class=""page-header"">
  <h2>Categories</h2>
  <a href=""/admin/categories/new"" class=""btn"">+ New category</a>
</div>
<table class=""admin-table"">
  <thead><tr>
    <th>ID</th><th>Name</th><th>Slug</th><th>Products</th><th>Status</th><th></th>
  </tr></thead>
  <tbody>{rows}</tbody>
</table>";

        return Render.AdminPage(ctx, "/admin/categories", html, "Categories");
    }

    // -------- Form (new / edit) --------
    public static async Task<IResult> FormAsync(HttpContext ctx, AppDbContext db, long? id)
    {
        Category? c = null;
        if (id is { } cid)
        {
            c = await db.Categories.FirstOrDefaultAsync(x => x.Id == cid);
            if (c is null) return Results.NotFound();
        }

        var action = c is null ? "/admin/categories" : $"/admin/categories/{c.Id}";
        var title = c is null ? "New category" : $"Edit category #{c.Id}";

        var html = $@"
<h2>{Layout.H(title)}</h2>
<form method=""post"" action=""{action}"">
  <div>
    <label for=""name"">Name</label>
    <input id=""name"" name=""name"" type=""text"" required maxlength=""120"" value=""{Layout.H(c?.Name)}"">
  </div>
  <div>
    <label for=""slug"">Slug (URL-friendly, unique, lowercase-with-dashes)</label>
    <input id=""slug"" name=""slug"" type=""text"" required maxlength=""140"" value=""{Layout.H(c?.Slug)}"" pattern=""[a-z0-9]+(-[a-z0-9]+)*"">
  </div>
  <div>
    <label for=""description"">Description</label>
    <input id=""description"" name=""description"" type=""text"" value=""{Layout.H(c?.Description)}"">
  </div>
  <div>
    <label><input type=""checkbox"" name=""isActive"" value=""true"" {(c?.IsActive ?? true ? "checked" : "")}> Active</label>
  </div>
  <div class=""form-actions"">
    <button type=""submit"">Save</button>
    <a href=""/admin/categories"" class=""btn-secondary"">Cancel</a>
  </div>
</form>";

        return Render.AdminPage(ctx, "/admin/categories", html, title);
    }

    // -------- Create --------
    public static async Task<IResult> CreateAsync(HttpContext ctx, AppDbContext db, AuditLogger audit)
    {
        var form = await ctx.Request.ReadFormAsync();
        var name = form["name"].ToString().Trim();
        var slug = form["slug"].ToString().Trim().ToLowerInvariant();
        var description = form["description"].ToString().Trim();
        var isActive = form["isActive"].ToString() == "true";

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug))
            return Results.BadRequest("Name and slug are required.");

        if (await db.Categories.AnyAsync(c => c.Slug == slug))
            return Results.BadRequest($"Slug '{slug}' is already in use.");

        var category = new Category
        {
            Name = name,
            Slug = slug,
            Description = string.IsNullOrWhiteSpace(description) ? null : description,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        await audit.LogAsync(GetAdminId(ctx), "category.create", "category", category.Id, new { name, slug });

        return Results.Redirect("/admin/categories");
    }

    // -------- Update --------
    public static async Task<IResult> UpdateAsync(HttpContext ctx, AppDbContext db, AuditLogger audit, long id)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id);
        if (category is null) return Results.NotFound();

        var form = await ctx.Request.ReadFormAsync();
        var name = form["name"].ToString().Trim();
        var slug = form["slug"].ToString().Trim().ToLowerInvariant();
        var description = form["description"].ToString().Trim();
        var isActive = form["isActive"].ToString() == "true";

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug))
            return Results.BadRequest("Name and slug are required.");

        if (await db.Categories.AnyAsync(c => c.Slug == slug && c.Id != id))
            return Results.BadRequest($"Slug '{slug}' is already in use.");

        category.Name = name;
        category.Slug = slug;
        category.Description = string.IsNullOrWhiteSpace(description) ? null : description;
        category.IsActive = isActive;
        await db.SaveChangesAsync();

        await audit.LogAsync(GetAdminId(ctx), "category.update", "category", category.Id, new { name, slug, isActive });

        return Results.Redirect("/admin/categories");
    }

    // -------- Soft-delete (deactivate) --------
    public static async Task<IResult> DeleteAsync(HttpContext ctx, AppDbContext db, AuditLogger audit, long id)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id);
        if (category is null) return Results.NotFound();

        category.IsActive = false;
        await db.SaveChangesAsync();

        await audit.LogAsync(GetAdminId(ctx), "category.deactivate", "category", category.Id, new { slug = category.Slug });

        return Results.Redirect("/admin/categories");
    }

    private static long? GetAdminId(HttpContext ctx)
    {
        var idStr = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(idStr, out var parsed) ? parsed : null;
    }
}