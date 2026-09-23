using EcommercePortfolio.Api.Html;
using EcommercePortfolio.Data;
using Microsoft.EntityFrameworkCore;

namespace EcommercePortfolio.Api.Endpoints;

public static class DashboardEndpoints
{
    public static async Task<IResult> IndexAsync(HttpContext ctx, AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

        var productCount = await db.Products.CountAsync(p => p.IsActive);
        var totalOrders  = await db.Orders.CountAsync();
        var pending      = await db.Orders.CountAsync(o => o.Status == "pending");
        var paid         = await db.Orders.CountAsync(o => o.Status == "paid");

        var revenueMonth = await db.Orders
            .Where(o => o.CreatedAt >= monthStart
                     && (o.Status == "paid" || o.Status == "processing"
                      || o.Status == "shipped" || o.Status == "delivered"))
            .SumAsync(o => (decimal?)o.Total) ?? 0m;

        var recent = await db.Orders
            .OrderByDescending(o => o.Id)
            .Take(5)
            .Select(o => new { o.Id, o.OrderNumber, o.FullName, o.Total, o.Status, o.CreatedAt })
            .ToListAsync();

        var recentHtml = recent.Count == 0
            ? "<p class=\"muted\">No orders yet.</p>"
            : "<table class=\"admin-table\"><thead><tr>" +
              "<th>Order</th><th>Customer</th><th>Total</th><th>Status</th><th>Date</th>" +
              "</tr></thead><tbody>" +
              string.Concat(recent.Select(o =>
                  $"<tr>" +
                  $"<td><a href=\"/admin/orders/{o.Id}\">{Layout.H(o.OrderNumber)}</a></td>" +
                  $"<td>{Layout.H(o.FullName)}</td>" +
                  $"<td>${o.Total:F2}</td>" +
                  $"<td><span class=\"badge badge-{Layout.H(o.Status)}\">{Layout.H(o.Status)}</span></td>" +
                  $"<td>{o.CreatedAt:yyyy-MM-dd HH:mm}</td>" +
                  $"</tr>"))
              + "</tbody></table>";

        var html = $@"
<h2>Dashboard</h2>
<div class=""cards"">
  <div class=""card""><div class=""card-label"">Active products</div><div class=""card-value"">{productCount}</div></div>
  <div class=""card""><div class=""card-label"">Total orders</div><div class=""card-value"">{totalOrders}</div></div>
  <div class=""card""><div class=""card-label"">Pending</div><div class=""card-value"">{pending}</div></div>
  <div class=""card""><div class=""card-label"">Paid</div><div class=""card-value"">{paid}</div></div>
  <div class=""card""><div class=""card-label"">Revenue this month</div><div class=""card-value"">${revenueMonth:F2}</div></div>
</div>

<h3>Recent orders</h3>
{recentHtml}
";

        return Render.AdminPage(ctx, "/admin", html, "Admin Dashboard");
    }
}