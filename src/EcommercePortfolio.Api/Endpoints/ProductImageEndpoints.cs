using EcommercePortfolio.Api.Auth;
using EcommercePortfolio.Api.Html;
using EcommercePortfolio.Data;
using EcommercePortfolio.Domain.Entities;
using EcommercePortfolio.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;

namespace EcommercePortfolio.Api.Endpoints;

public static class ProductImageEndpoints
{
    // ---- Full page ----
    public static async Task<IResult> PageAsync(HttpContext ctx, AppDbContext db, long id)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return Results.NotFound();

        var grid = await RenderGridAsync(db, id);

        var html = $@"
<div class=""page-header"">
  <h2>Images — {Layout.H(product.Name)}</h2>
  <a href=""/admin/products/{id}/edit"" class=""btn-secondary"">← Back to product</a>
</div>
<div id=""images"">{grid}</div>";

        return Render.AdminPage(ctx, "/admin/products", html, $"Images — {product.Name}");
    }

    // ---- Just the grid (HTMX partial) ----
    public static async Task<IResult> FragmentAsync(HttpContext ctx, AppDbContext db, long id)
    {
        var exists = await db.Products.AnyAsync(p => p.Id == id);
        if (!exists) return Results.NotFound();

        var grid = await RenderGridAsync(db, id);
        return Results.Content($"<div id=\"images\">{grid}</div>", "text/html; charset=utf-8");
    }

    // ---- Upload (multi-file) ----
    public static async Task<IResult> UploadAsync(
        HttpContext ctx, AppDbContext db, AuditLogger audit,
        IImageStorage storage, long id)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return Results.NotFound();

        var form = await ctx.Request.ReadFormAsync();
        var files = form.Files;
        if (files.Count == 0)
            return await FragmentWithError(ctx, db, id, "No files selected.");

        var errors = new List<string>();
        var anyPrimary = await db.ProductImages.AnyAsync(i => i.ProductId == id && i.IsPrimary);
        var nextSort = (await db.ProductImages
            .Where(i => i.ProductId == id)
            .Select(i => (int?)i.SortOrder)
            .MaxAsync() ?? -1) + 1;

        foreach (var file in files)
        {
            var result = await storage.StoreAsync(id, file, ctx.RequestAborted);
            if (!result.Success)
            {
                errors.Add($"{file.FileName}: {result.Error}");
                continue;
            }

            var image = new ProductImage
            {
                ProductId = id,
                Url = result.Url!,
                AltText = product.Name,
                SortOrder = nextSort++,
                IsPrimary = !anyPrimary
            };
            db.ProductImages.Add(image);
            anyPrimary = anyPrimary || image.IsPrimary;
        }

        if (errors.Count > 0 && !await db.ProductImages.AnyAsync(i => i.ProductId == id))
        {
            // Nothing saved at all — show errors
            return await FragmentWithError(ctx, db, id, string.Join(" · ", errors));
        }

        await db.SaveChangesAsync();

        await audit.LogAsync(GetAdminId(ctx), "product.image.upload", "product", id,
            new { count = files.Count - errors.Count, errors });

        var grid = await RenderGridAsync(db, id);
        var errorBlock = errors.Count > 0
            ? $"<p class=\"error\">{Layout.H(string.Join(" · ", errors))}</p>"
            : "";
        return Results.Content($"<div id=\"images\">{errorBlock}{grid}</div>", "text/html; charset=utf-8");
    }

    // ---- Set primary ----
    public static async Task<IResult> SetPrimaryAsync(
        HttpContext ctx, AppDbContext db, AuditLogger audit, long id, long imageId)
    {
        var images = await db.ProductImages.Where(i => i.ProductId == id).ToListAsync();
        var target = images.FirstOrDefault(i => i.Id == imageId);
        if (target is null) return Results.NotFound();

        foreach (var img in images) img.IsPrimary = (img.Id == imageId);
        await db.SaveChangesAsync();

        await audit.LogAsync(GetAdminId(ctx), "product.image.set_primary", "product", id,
            new { imageId });

        var grid = await RenderGridAsync(db, id);
        return Results.Content($"<div id=\"images\">{grid}</div>", "text/html; charset=utf-8");
    }

    // ---- Delete ----
    public static async Task<IResult> DeleteAsync(
        HttpContext ctx, AppDbContext db, AuditLogger audit,
        IImageStorage storage, long id, long imageId)
    {
        var image = await db.ProductImages.FirstOrDefaultAsync(i => i.Id == imageId && i.ProductId == id);
        if (image is null) return Results.NotFound();

        var wasPrimary = image.IsPrimary;
        var url = image.Url;

        db.ProductImages.Remove(image);
        await db.SaveChangesAsync();

        await storage.DeleteAsync(url, ctx.RequestAborted);

        // If we deleted the primary, promote the oldest remaining image
        if (wasPrimary)
        {
            var next = await db.ProductImages
                .Where(i => i.ProductId == id)
                .OrderBy(i => i.SortOrder)
                .FirstOrDefaultAsync();
            if (next is not null)
            {
                next.IsPrimary = true;
                await db.SaveChangesAsync();
            }
        }

        await audit.LogAsync(GetAdminId(ctx), "product.image.delete", "product", id,
            new { imageId, url });

        var grid = await RenderGridAsync(db, id);
        return Results.Content($"<div id=\"images\">{grid}</div>", "text/html; charset=utf-8");
    }

    // ---- Helpers ----
    private static async Task<IResult> FragmentWithError(HttpContext ctx, AppDbContext db, long id, string error)
    {
        var grid = await RenderGridAsync(db, id);
        return Results.Content(
            $"<div id=\"images\"><p class=\"error\">{Layout.H(error)}</p>{grid}</div>",
            "text/html; charset=utf-8");
    }

    private static async Task<string> RenderGridAsync(AppDbContext db, long productId)
    {
        var images = await db.ProductImages
            .Where(i => i.ProductId == productId)
            .OrderByDescending(i => i.IsPrimary)
            .ThenBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .ToListAsync();

        var sb = new StringBuilder();

        // ---- Upload zone ----
        sb.Append(@"<div class=""upload-zone"" id=""upload-zone"">
  <form id=""upload-form"" method=""post"" action=""/admin/products/").Append(productId).Append(@"/images""
        enctype=""multipart/form-data""
        hx-post=""/admin/products/").Append(productId).Append(@"/images""
        hx-encoding=""multipart/form-data""
        hx-target=""#images"" hx-swap=""outerHTML"">
    <label for=""file-input"" class=""drop-label"">
      <span class=""drop-icon"">📁</span>
      <span>Drop images here, or <u>click to browse</u></span>
      <span class=""drop-hint"">1:1 (square) recommended · Min 800×800 · Max 4000×4000 · Max 5 MB · JPEG/PNG/WebP</span>
    </label>
    <input id=""file-input"" name=""files"" type=""file"" accept=""image/jpeg,image/png,image/webp"" multiple style=""display:none"">
    <div id=""file-list"" class=""file-list""></div>
    <button type=""submit"" id=""upload-btn"" style=""display:none"">Upload</button>
  </form>
</div>");

        // ---- Header ----
        sb.Append("<h3>Current images (").Append(images.Count).Append(")</h3>");

        // ---- Grid or empty ----
        if (images.Count == 0)
        {
            sb.Append("<p class=\"muted\">No images yet. Upload one below.</p>");
        }
        else
        {
            sb.Append("<div class=\"image-grid\">");
            foreach (var img in images)
            {
                var url = Layout.H(img.Url);
                var alt = Layout.H(img.AltText);
                var cardClass = img.IsPrimary ? "image-card primary" : "image-card";

                sb.Append("<div class=\"").Append(cardClass).Append("\">");
                sb.Append("<div class=\"image-thumb\"><img src=\"").Append(url)
                  .Append("\" alt=\"").Append(alt).Append("\"></div>");
                sb.Append("<div class=\"image-card-actions\">");

                if (img.IsPrimary)
                {
                    sb.Append("<span class=\"badge badge-paid\">Primary</span>");
                }
                else
                {
                    sb.Append("<form method=\"post\" action=\"/admin/products/")
                      .Append(productId).Append("/images/").Append(img.Id).Append("/primary\" ")
                      .Append("style=\"display:inline\" ")
                      .Append("hx-post=\"/admin/products/").Append(productId)
                      .Append("/images/").Append(img.Id).Append("/primary\" ")
                      .Append("hx-target=\"#images\" hx-swap=\"outerHTML\">")
                      .Append("<button type=\"submit\" class=\"link\">Set primary</button>")
                      .Append("</form>");
                }

                sb.Append("<form method=\"post\" action=\"/admin/products/")
                  .Append(productId).Append("/images/").Append(img.Id).Append("/delete\" ")
                  .Append("style=\"display:inline\" ")
                  .Append("hx-post=\"/admin/products/").Append(productId)
                  .Append("/images/").Append(img.Id).Append("/delete\" ")
                  .Append("hx-target=\"#images\" hx-swap=\"outerHTML\" ")
                  .Append("onsubmit=\"return confirm('Delete this image?');\">")
                  .Append("<button type=\"submit\" class=\"link danger\">Delete</button>")
                  .Append("</form>");

                sb.Append("</div></div>");
            }
            sb.Append("</div>");
        }

        // ---- Client script (raw, no interpolation) ----
        sb.Append(@"<script>
(function () {
  const input = document.getElementById('file-input');
  const list  = document.getElementById('file-list');
  const btn   = document.getElementById('upload-btn');
  const zone  = document.getElementById('upload-zone');
  if (!input) return;

  const MAX_BYTES = 5 * 1024 * 1024;

  function renderList() {
    list.innerHTML = '';
    const files = Array.from(input.files || []);
    if (files.length === 0) { btn.style.display = 'none'; return; }
    files.forEach(f => {
      const div = document.createElement('div');
      div.className = 'file-item';
      const ok = f.size <= MAX_BYTES;
      div.textContent = (ok ? '\u2713 ' : '\u2717 ') + f.name + ' (' + (f.size / 1024).toFixed(0) + ' KB)' +
        (ok ? '' : ' \u2014 too large (max 5 MB)');
      if (!ok) div.classList.add('file-item-error');
      list.appendChild(div);
    });
    btn.style.display = 'inline-block';
  }

  input.addEventListener('change', renderList);

  ['dragenter','dragover'].forEach(ev => zone.addEventListener(ev, e => { e.preventDefault(); zone.classList.add('dragging'); }));
  ['dragleave','drop'].forEach(ev => zone.addEventListener(ev, e => { e.preventDefault(); zone.classList.remove('dragging'); }));
  zone.addEventListener('drop', e => {
    if (e.dataTransfer && e.dataTransfer.files.length) {
      input.files = e.dataTransfer.files;
      renderList();
    }
  });
})();
</script>");

        return sb.ToString();
    }

    private static long? GetAdminId(HttpContext ctx)
    {
        var idStr = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(idStr, out var parsed) ? parsed : null;
    }
}