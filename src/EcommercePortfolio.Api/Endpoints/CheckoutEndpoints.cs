using EcommercePortfolio.Api.Auth;
using EcommercePortfolio.Api.Html;
using EcommercePortfolio.Data;
using EcommercePortfolio.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;

namespace EcommercePortfolio.Api.Endpoints;

public static class CheckoutEndpoints
{
    // GET /checkout
    public static async Task<IResult> FormAsync(HttpContext ctx, AppDbContext db)
    {
        var cart = await CartIdentity.GetCartAsync(ctx, db);
        if (cart is null || cart.Items.Count == 0)
            return Results.Redirect("/cart");

        var html = BuildForm(null, cart);
        return Render.StorePage(ctx, "/checkout", html, "Checkout — Atelier");
    }

    // POST /checkout → creates order, redirects to Stripe Checkout
    public static async Task<IResult> SubmitAsync(
        HttpContext ctx, AppDbContext db, IConfiguration config)
    {
        var cart = await CartIdentity.GetCartAsync(ctx, db);
        if (cart is null || cart.Items.Count == 0)
            return Results.Redirect("/cart");

        var form = await ctx.Request.ReadFormAsync();
        var email = form["email"].ToString().Trim();
        var fullName = form["fullName"].ToString().Trim();
        var phone = form["phone"].ToString().Trim();
        var shippingAddress = form["shippingAddress"].ToString().Trim();
        var city = form["city"].ToString().Trim();
        var postalCode = form["postalCode"].ToString().Trim();
        var country = form["country"].ToString().Trim();

        var error = Validate(email, fullName, shippingAddress, city, country);
        if (error is not null)
            return Render.StorePage(ctx, "/checkout", BuildForm(error, cart), "Checkout — Atelier");

        decimal subtotal = cart.Items.Sum(i => i.Product!.Price * i.Quantity);
        const decimal shipping = 0m;
        decimal total = subtotal + shipping;

        await using var tx = await db.Database.BeginTransactionAsync();

        // Serialize order-number generation
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(987654321)");

        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        var prefix = $"ORD-{today}-";
        var countToday = await db.Orders.CountAsync(o => o.OrderNumber.StartsWith(prefix));
        var orderNumber = $"{prefix}{countToday + 1:D4}";

        var now = DateTimeOffset.UtcNow;
        var order = new Order
        {
            OrderNumber = orderNumber,
            Email = email,
            FullName = fullName,
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone,
            ShippingAddress = shippingAddress,
            City = city,
            PostalCode = string.IsNullOrWhiteSpace(postalCode) ? null : postalCode,
            Country = country,
            Subtotal = subtotal,
            ShippingFee = shipping,
            Total = total,
            Status = "pending",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        foreach (var item in cart.Items)
        {
            var p = item.Product!;
            db.OrderItems.Add(new OrderItem
            {
                OrderId = order.Id,
                ProductId = p.Id,
                ProductNameSnapshot = p.Name,
                UnitPriceSnapshot = p.Price,
                Quantity = item.Quantity,
                LineTotal = p.Price * item.Quantity
            });
        }
        await db.SaveChangesAsync();

        // Atomic stock reservation. If any line fails, roll back the whole order.
        if (!await TryDecrementStockAsync(db, cart.Items))
        {
            await tx.RollbackAsync();
            return Render.StorePage(ctx, "/checkout",
                BuildForm("One or more items are out of stock. Please review your cart.", cart),
                "Checkout — Atelier");
        }

        // ---- Create Stripe Checkout Session ----
        var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
        var lineItems = order.Items.Count == 0
            ? cart.Items.Select(i => new Stripe.Checkout.SessionLineItemOptions
            {
                Quantity = i.Quantity,
                PriceData = new Stripe.Checkout.SessionLineItemPriceDataOptions
                {
                    Currency = "usd",
                    UnitAmount = (long)(i.Product!.Price * 100),
                    ProductData = new Stripe.Checkout.SessionLineItemPriceDataProductDataOptions
                    {
                        Name = i.Product!.Name
                    }
                }
            }).ToList()
            : (await db.OrderItems.Where(oi => oi.OrderId == order.Id).ToListAsync())
                .Select(oi => new Stripe.Checkout.SessionLineItemOptions
                {
                    Quantity = oi.Quantity,
                    PriceData = new Stripe.Checkout.SessionLineItemPriceDataOptions
                    {
                        Currency = "usd",
                        UnitAmount = (long)(oi.UnitPriceSnapshot * 100),
                        ProductData = new Stripe.Checkout.SessionLineItemPriceDataProductDataOptions
                        {
                            Name = oi.ProductNameSnapshot
                        }
                    }
                }).ToList();

        Stripe.Checkout.Session session;
        try
        {
            var options = new Stripe.Checkout.SessionCreateOptions
            {
                Mode = "payment",
                LineItems = lineItems,
                CustomerEmail = email,
                SuccessUrl = $"{baseUrl}/checkout/success?order={orderNumber}&session_id={{CHECKOUT_SESSION_ID}}",
                CancelUrl  = $"{baseUrl}/checkout/cancelled?order={orderNumber}",
                ClientReferenceId = orderNumber,
                Metadata = new Dictionary<string, string>
                {
                    ["order_id"] = order.Id.ToString(),
                    ["order_number"] = orderNumber
                }
            };
            session = await new Stripe.Checkout.SessionService().CreateAsync(options);
        }
        catch (Stripe.StripeException ex)
        {
            await tx.RollbackAsync();
            return Render.StorePage(ctx, "/checkout",
                BuildForm($"Payment provider error: {ex.Message}", cart),
                "Checkout — Atelier");
        }

        db.Payments.Add(new Payment
        {
            OrderId = order.Id,
            Provider = "stripe",
            ProviderPaymentId = session.Id,
            Amount = total,
            Currency = "usd",
            Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow
        });

        // Clear cart now (the webhook will confirm payment separately)
        db.CartItems.RemoveRange(cart.Items);
        cart.UpdatedAt = now;
        await db.SaveChangesAsync();

        await tx.CommitAsync();

        ctx.Response.Headers.Append("HX-Trigger", "cart-updated");
        return Results.Redirect(session.Url);
    }

    // GET /checkout/success?order=ORD-...&session_id=cs_test_...
    public static async Task<IResult> SuccessAsync(HttpContext ctx, AppDbContext db, string? order)
    {
        if (string.IsNullOrWhiteSpace(order))
            return Results.Redirect("/");

        var o = await db.Orders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.OrderNumber == order);
        if (o is null) return Results.Redirect("/");

        ctx.Response.Headers.Append("HX-Trigger", "cart-updated");
        return Render.StorePage(ctx, "/checkout", BuildSuccess(o), "Order — Atelier");
    }

    // GET /checkout/cancelled
    public static IResult CancelledAsync(HttpContext ctx, string? order)
    {
        var orderLine = string.IsNullOrWhiteSpace(order)
            ? ""
            : $"<b>{Layout.H(order)}</b> ";

        var html = $@"
<div class=""checkout-result"">
  <h1 class=""store-page-title"">Checkout cancelled</h1>
  <p class=""store-page-sub"">Your order {orderLine}was not completed. Your cart is still saved.</p>
  <div class=""checkout-result-actions"">
    <a href=""/cart"" class=""store-btn store-btn-primary"">Back to cart</a>
    <a href=""/products"" class=""store-btn store-btn-ghost"">Continue shopping</a>
  </div>
</div>";
        return Render.StorePage(ctx, "/checkout", html, "Checkout cancelled — Atelier");
    }

    // POST /webhooks/stripe
    public static async Task<IResult> StripeWebhookAsync(
        HttpContext ctx, AppDbContext db, IConfiguration config, ILoggerFactory logFactory)
    {
        var log = logFactory.CreateLogger("StripeWebhook");
        var json = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
        var sigHeader = ctx.Request.Headers["Stripe-Signature"].ToString();
        var secret = config["Stripe:WebhookSecret"];

        Stripe.Event stripeEvent;
        try
        {
            stripeEvent = Stripe.EventUtility.ConstructEvent(json, sigHeader, secret);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Stripe webhook signature verification failed");
            return Results.BadRequest("Invalid signature");
        }

        log.LogInformation("Stripe webhook received: {Type} ({Id})", stripeEvent.Type, stripeEvent.Id);

        if (stripeEvent.Type == "checkout.session.completed")
        {
            var session = (Stripe.Checkout.Session)stripeEvent.Data.Object;
            await HandleSessionCompletedAsync(db, session, json, log);
        }
        else if (stripeEvent.Type == "checkout.session.expired" ||
                 stripeEvent.Type == "checkout.session.async_payment_failed")
        {
            var session = (Stripe.Checkout.Session)stripeEvent.Data.Object;
            await HandleSessionFailedAsync(db, session, log);
        }

        return Results.Ok();
    }

    private static async Task HandleSessionCompletedAsync(
        AppDbContext db, Stripe.Checkout.Session session, string rawJson, ILogger log)
    {
        if (session.Metadata is null || !session.Metadata.TryGetValue("order_id", out var idStr)
            || !long.TryParse(idStr, out var orderId))
        {
            log.LogWarning("Stripe webhook missing order_id in metadata");
            return;
        }

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null)
        {
            log.LogWarning("Stripe webhook: order {OrderId} not found", orderId);
            return;
        }

        var payment = await db.Payments
            .FirstOrDefaultAsync(p => p.ProviderPaymentId == session.Id);

        // Idempotency: if we've already processed this payment, do nothing.
        if (payment is not null && payment.Status == "succeeded")
        {
            log.LogInformation("Stripe webhook: payment {Session} already processed", session.Id);
            return;
        }

        if (payment is null)
        {
            payment = new Payment
            {
                OrderId = order.Id,
                Provider = "stripe",
                ProviderPaymentId = session.Id,
                Amount = (session.AmountTotal ?? 0) / 100m,
                Currency = session.Currency ?? "usd",
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Payments.Add(payment);
        }

        payment.Status = "succeeded";
        payment.RawResponseJson = rawJson;

        // Only transition forward from pending / paid-awaiting
        if (order.Status == "pending")
            order.Status = "paid";

        order.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        log.LogInformation("Order {OrderNumber} marked paid", order.OrderNumber);
    }

    private static async Task HandleSessionFailedAsync(
        AppDbContext db, Stripe.Checkout.Session session, ILogger log)
    {
        if (session.Metadata is null || !session.Metadata.TryGetValue("order_id", out var idStr)
            || !long.TryParse(idStr, out var orderId))
            return;

        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order is null) return;
        if (order.Status != "pending") return; // already resolved

        // Restore stock
        foreach (var item in order.Items)
        {
            if (item.ProductId is null) continue;
            await db.Database.ExecuteSqlInterpolatedAsync(
                $@"UPDATE products
                   SET stock = stock + {item.Quantity},
                       updated_at = now()
                   WHERE id = {item.ProductId}");
        }

        order.Status = "cancelled";
        order.UpdatedAt = DateTimeOffset.UtcNow;

        var payment = await db.Payments
            .FirstOrDefaultAsync(p => p.ProviderPaymentId == session.Id);

        if (payment is not null) payment.Status = "failed";

        await db.SaveChangesAsync();

        log.LogInformation("Order {OrderNumber} cancelled + stock restored", order.OrderNumber);
    }

    // ============================================================
    // HELPERS
    // ============================================================
    private static string? Validate(string email, string fullName, string addr, string city, string country)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) return "Please enter a valid email.";
        if (string.IsNullOrWhiteSpace(fullName)) return "Please enter your full name.";
        if (string.IsNullOrWhiteSpace(addr)) return "Please enter a shipping address.";
        if (string.IsNullOrWhiteSpace(city)) return "Please enter a city.";
        if (string.IsNullOrWhiteSpace(country)) return "Please enter a country.";
        return null;
    }

    private static async Task<bool> TryDecrementStockAsync(AppDbContext db, List<CartItem> items)
    {
        foreach (var item in items)
        {
            var rows = await db.Database.ExecuteSqlInterpolatedAsync(
                $@"UPDATE products
                   SET stock = stock - {item.Quantity},
                       updated_at = now()
                   WHERE id = {item.ProductId}
                     AND stock >= {item.Quantity}");
            if (rows == 0) return false;
        }
        return true;
    }

    // ============================================================
    // HTML BUILDERS
    // ============================================================
    private static string BuildForm(string? error, Cart cart)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(error))
            sb.Append("<p class=\"checkout-error\">").Append(Layout.H(error)).Append("</p>");

        sb.Append(@"
<div class=""checkout-layout"">
  <form class=""checkout-form"" method=""post"" action=""/checkout"">
    <h2 class=""checkout-section-title"">Contact</h2>
    <div class=""field""><label for=""email"">Email</label><input id=""email"" name=""email"" type=""email"" required></div>
    <div class=""field""><label for=""fullName"">Full name</label><input id=""fullName"" name=""fullName"" type=""text"" required maxlength=""200""></div>
    <div class=""field""><label for=""phone"">Phone (optional)</label><input id=""phone"" name=""phone"" type=""tel"" maxlength=""32""></div>

    <h2 class=""checkout-section-title"">Shipping</h2>
    <div class=""field""><label for=""shippingAddress"">Address</label><input id=""shippingAddress"" name=""shippingAddress"" type=""text"" required maxlength=""300""></div>
    <div class=""field-row"">
      <div class=""field""><label for=""city"">City</label><input id=""city"" name=""city"" type=""text"" required maxlength=""120""></div>
      <div class=""field""><label for=""postalCode"">Postal code (optional)</label><input id=""postalCode"" name=""postalCode"" type=""text"" maxlength=""20""></div>
    </div>
    <div class=""field""><label for=""country"">Country</label><input id=""country"" name=""country"" type=""text"" required maxlength=""80"" value=""Morocco""></div>

    <button type=""submit"" class=""checkout-submit"">Place order</button>
    <p class=""checkout-note"">Test mode · No real payment is processed.</p>
  </form>

  <aside class=""checkout-summary"">
    <h2 class=""cart-summary-title"">Order summary</h2>");

        decimal subtotal = 0m;
        foreach (var item in cart.Items)
        {
            var p = item.Product!;
            var line = p.Price * item.Quantity;
            subtotal += line;

            sb.Append("<div class=\"checkout-line\">")
              .Append("<span>").Append(Layout.H(p.Name))
              .Append(" <span class=\"muted\">× ").Append(item.Quantity).Append("</span></span>")
              .Append("<span>$").Append(line.ToString("F2")).Append("</span>")
              .Append("</div>");
        }

        sb.Append("<div class=\"cart-summary-divider\"></div>");
        sb.Append("<div class=\"cart-summary-row\"><span>Subtotal</span><span>$")
          .Append(subtotal.ToString("F2")).Append("</span></div>");
        sb.Append("<div class=\"cart-summary-row\"><span>Shipping</span><span>Free</span></div>");
        sb.Append("<div class=\"cart-summary-divider\"></div>");
        sb.Append("<div class=\"cart-summary-row cart-summary-total\"><span>Total</span><span>$")
          .Append(subtotal.ToString("F2")).Append("</span></div>");

        sb.Append(@"
  </aside>
</div>");
        return sb.ToString();
    }

    private static string BuildSuccess(Order o)
    {
        var isPaid = o.Status == "paid" || o.Status == "processing"
                  || o.Status == "shipped" || o.Status == "delivered";
        var header = isPaid
            ? "<div class=\"checkout-success-icon\">✓</div>"
            : "<div class=\"checkout-success-icon\" style=\"background:rgba(230,180,90,.18);color:#E2B860;\">⏳</div>";
        var title = isPaid ? "Order confirmed" : "Payment processing";
        var subtitle = isPaid
            ? $"Thanks, {Layout.H(o.FullName)}. A confirmation was sent to <b>{Layout.H(o.Email)}</b>."
            : $"Thanks, {Layout.H(o.FullName)}. We're waiting for Stripe to confirm your payment. This usually takes a few seconds — refresh to update.";

        var sb = new StringBuilder();
        sb.Append("<div class=\"checkout-result\">");
        sb.Append(header);
        sb.Append("<h1 class=\"store-page-title\">").Append(title).Append("</h1>");
        sb.Append("<p class=\"store-page-sub\">").Append(subtitle).Append("</p>");
        sb.Append("<div class=\"checkout-receipt\">");
        sb.Append("<div class=\"checkout-receipt-head\">");
        sb.Append("<span>Order <b>").Append(Layout.H(o.OrderNumber)).Append("</b></span>");
        sb.Append("<span class=\"badge badge-").Append(Layout.H(o.Status)).Append("\">")
          .Append(Layout.H(o.Status)).Append("</span>");
        sb.Append("</div>");
        foreach (var item in o.Items)
        {
            sb.Append("<div class=\"checkout-line\">")
              .Append("<span>").Append(Layout.H(item.ProductNameSnapshot))
              .Append(" <span class=\"muted\">× ").Append(item.Quantity).Append("</span></span>")
              .Append("<span>$").Append(item.LineTotal.ToString("F2")).Append("</span>")
              .Append("</div>");
        }
        sb.Append("<div class=\"cart-summary-divider\"></div>");
        sb.Append("<div class=\"cart-summary-row cart-summary-total\"><span>")
          .Append(isPaid ? "Total paid" : "Total due").Append("</span><span>$")
          .Append(o.Total.ToString("F2")).Append("</span></div>");
        sb.Append("</div>");
        sb.Append("<div class=\"checkout-result-actions\">");
        if (!isPaid)
            sb.Append("<a href=\"?order=").Append(Layout.H(o.OrderNumber))
              .Append("\" class=\"store-btn store-btn-ghost\">Refresh status</a>");
        sb.Append("<a href=\"/products\" class=\"store-btn store-btn-primary\">Continue shopping</a>");
        sb.Append("</div></div>");
        return sb.ToString();
    }
}