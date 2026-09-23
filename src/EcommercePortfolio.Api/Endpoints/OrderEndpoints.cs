using EcommercePortfolio.Api.Auth;
using EcommercePortfolio.Api.Html;
using EcommercePortfolio.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;

namespace EcommercePortfolio.Api.Endpoints;

public static class OrderEndpoints
{
    // ============================================================
    // LIST
    // ============================================================
    public static async Task<IResult> ListAsync(HttpContext ctx, AppDbContext db, string? status)
    {
        var query = db.Orders.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(o => o.Status == status);

        var orders = await query
            .OrderByDescending(o => o.Id)
            .Take(100)
            .Select(o => new
            {
                o.Id, o.OrderNumber, o.Email, o.FullName,
                o.Total, o.Status, o.CreatedAt,
                ItemCount = o.Items.Count
            })
            .ToListAsync();

        var tabs = new (string Key, string Label)[]
        {
            ("", "All"), ("pending", "Pending"), ("paid", "Paid"),
            ("processing", "Processing"), ("shipped", "Shipped"),
            ("delivered", "Delivered"), ("cancelled", "Cancelled"), ("refunded", "Refunded")
        };

        var tabHtml = new StringBuilder();
        tabHtml.Append("<nav class=\"cat-tabs\" style=\"margin-bottom:1.5rem;\">");
        foreach (var (key, label) in tabs)
        {
            var active = (status ?? "") == key;
            var url = string.IsNullOrEmpty(key) ? "/admin/orders" : $"/admin/orders?status={key}";
            tabHtml.Append("<a href=\"").Append(url).Append("\" class=\"cat-tab")
                .Append(active ? " active" : "").Append("\">").Append(label).Append("</a>");
        }
        tabHtml.Append("</nav>");

        var rows = orders.Count == 0
            ? "<tr><td colspan=\"7\" class=\"muted\">No orders yet.</td></tr>"
            : string.Concat(orders.Select(o =>
                "<tr>" +
                $"<td><a href=\"/admin/orders/{o.Id}\">{Layout.H(o.OrderNumber)}</a></td>" +
                $"<td>{Layout.H(o.FullName)}<br><span class=\"muted\" style=\"font-size:.82rem;\">{Layout.H(o.Email)}</span></td>" +
                $"<td>{o.ItemCount}</td>" +
                $"<td>${o.Total:F2}</td>" +
                $"<td><span class=\"badge badge-{Layout.H(o.Status)}\">{Layout.H(o.Status)}</span></td>" +
                $"<td>{o.CreatedAt:yyyy-MM-dd HH:mm}</td>" +
                $"<td><a href=\"/admin/orders/{o.Id}\">View</a></td>" +
                "</tr>"));

        var html = $@"
<div class=""page-header"">
  <h2>Orders</h2>
</div>
{tabHtml}
<table class=""admin-table"">
  <thead><tr>
    <th>Order</th><th>Customer</th><th>Items</th><th>Total</th><th>Status</th><th>Date</th><th></th>
  </tr></thead>
  <tbody>{rows}</tbody>
</table>";

        return Render.AdminPage(ctx, "/admin/orders", html, "Orders");
    }

    // ============================================================
    // DETAIL
    // ============================================================
    public static async Task<IResult> DetailAsync(HttpContext ctx, AppDbContext db, long id)
    {
        var o = await db.Orders
            .Include(x => x.Items)
            .Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (o is null) return Results.NotFound();

        var itemRows = new StringBuilder();
        foreach (var it in o.Items)
        {
            itemRows.Append("<tr>")
                .Append("<td>").Append(Layout.H(it.ProductNameSnapshot)).Append("</td>")
                .Append("<td>$").Append(it.UnitPriceSnapshot.ToString("F2")).Append("</td>")
                .Append("<td>").Append(it.Quantity).Append("</td>")
                .Append("<td>$").Append(it.LineTotal.ToString("F2")).Append("</td>")
                .Append("</tr>");
        }

        var paymentsHtml = o.Payments.Count == 0
            ? "<p class=\"muted\">No payment records yet.</p>"
            : "<table class=\"admin-table\"><thead><tr><th>Provider</th><th>ID</th><th>Amount</th><th>Status</th><th>Created</th></tr></thead><tbody>" +
              string.Concat(o.Payments.Select(p =>
                  "<tr>" +
                  $"<td>{Layout.H(p.Provider)}</td>" +
                  $"<td><code style=\"font-size:.82rem;\">{Layout.H(p.ProviderPaymentId)}</code></td>" +
                  $"<td>${p.Amount:F2} {Layout.H(p.Currency)}</td>" +
                  $"<td><span class=\"badge badge-{Layout.H(p.Status)}\">{Layout.H(p.Status)}</span></td>" +
                  $"<td>{p.CreatedAt:yyyy-MM-dd HH:mm}</td>" +
                  "</tr>")) +
              "</tbody></table>";

        var statusSelect = $@"
<form method=""post"" action=""/admin/orders/{id}/status"" style=""display:flex;gap:.5rem;align-items:center;margin-top:1rem;"">
  <label style=""font-size:.85rem;color:#666;"">Change status:</label>
  <select name=""status"" style=""padding:.4rem .6rem;border:1px solid #ccc;border-radius:4px;"">
    {string.Concat(new[] { "pending", "paid", "processing", "shipped", "delivered", "cancelled", "refunded" }
        .Select(s => $"<option value=\"{s}\" {(o.Status == s ? "selected" : "")}>{s}</option>"))}
  </select>
  <button type=""submit"" class=""btn"">Update</button>
</form>";

        var html = $@"
<div class=""page-header"">
  <h2>Order {Layout.H(o.OrderNumber)}</h2>
  <a href=""/admin/orders"" class=""btn-secondary"">← Back</a>
</div>

<div class=""cards"" style=""grid-template-columns:repeat(auto-fit,minmax(180px,1fr));margin-bottom:1.5rem;"">
  <div class=""card""><div class=""card-label"">Status</div><div class=""card-value"" style=""font-size:1.1rem;"">
    <span class=""badge badge-{Layout.H(o.Status)}"">{Layout.H(o.Status)}</span></div></div>
  <div class=""card""><div class=""card-label"">Total</div><div class=""card-value"">${o.Total:F2}</div></div>
  <div class=""card""><div class=""card-label"">Placed</div><div class=""card-value"" style=""font-size:1rem;"">{o.CreatedAt:yyyy-MM-dd HH:mm}</div></div>
</div>

<h3>Customer</h3>
<table class=""admin-table"" style=""margin-bottom:1.5rem;"">
  <tbody>
    <tr><th style=""width:180px;"">Email</th><td>{Layout.H(o.Email)}</td></tr>
    <tr><th>Name</th><td>{Layout.H(o.FullName)}</td></tr>
    <tr><th>Phone</th><td>{(string.IsNullOrEmpty(o.Phone) ? "<span class=\"muted\">—</span>" : Layout.H(o.Phone))}</td></tr>
    <tr><th>Address</th><td>{Layout.H(o.ShippingAddress)}</td></tr>
    <tr><th>City</th><td>{Layout.H(o.City)}</td></tr>
    <tr><th>Postal code</th><td>{(string.IsNullOrEmpty(o.PostalCode) ? "<span class=\"muted\">—</span>" : Layout.H(o.PostalCode))}</td></tr>
    <tr><th>Country</th><td>{Layout.H(o.Country)}</td></tr>
  </tbody>
</table>

<h3>Items</h3>
<table class=""admin-table"" style=""margin-bottom:1.5rem;"">
  <thead><tr><th>Product</th><th>Unit price</th><th>Qty</th><th>Line total</th></tr></thead>
  <tbody>{itemRows}</tbody>
  <tfoot>
    <tr><th colspan=""3"" style=""text-align:right;"">Subtotal</th><td>${o.Subtotal:F2}</td></tr>
    <tr><th colspan=""3"" style=""text-align:right;"">Shipping</th><td>${o.ShippingFee:F2}</td></tr>
    <tr><th colspan=""3"" style=""text-align:right;"">Total</th><td><b>${o.Total:F2}</b></td></tr>
  </tfoot>
</table>

<h3>Payment</h3>
{paymentsHtml}

{statusSelect}";

        return Render.AdminPage(ctx, "/admin/orders", html, $"Order {o.OrderNumber}");
    }

    // ============================================================
    // UPDATE STATUS
    // ============================================================
    public static async Task<IResult> UpdateStatusAsync(
        HttpContext ctx, AppDbContext db, AuditLogger audit, long id)
    {
        var o = await db.Orders.FirstOrDefaultAsync(x => x.Id == id);
        if (o is null) return Results.NotFound();

        var form = await ctx.Request.ReadFormAsync();
        var status = form["status"].ToString().Trim().ToLowerInvariant();

        var valid = new[] { "pending", "paid", "processing", "shipped", "delivered", "cancelled", "refunded" };
        if (!valid.Contains(status))
            return Results.BadRequest("Invalid status");

        var previous = o.Status;
        o.Status = status;
        o.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var idStr = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        long? adminId = long.TryParse(idStr, out var parsed) ? parsed : null;
        await audit.LogAsync(adminId, "order.status.update", "order", id,
            new { from = previous, to = status });

        return Results.Redirect($"/admin/orders/{id}");
    }
}