using EcommercePortfolio.Api.Auth;
using EcommercePortfolio.Api.Endpoints;
using EcommercePortfolio.Api.Html;
using EcommercePortfolio.Data;
using EcommercePortfolio.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Stripe;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// ---- DB ----
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default"))
       .UseSnakeCaseNamingConvention());

// ---- Stripe ----
StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
    options.Cookie.Name = "__Host-csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // dev-friendly
    options.Cookie.SameSite = SameSiteMode.Strict;
});

builder.Services.AddScoped<IImageStorage, LocalImageStorage>();

// ---- Auth services ----
builder.Services.AddScoped<AdminAuthService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditLogger>();
builder.Services.AddAdminAuth();
builder.Services.AddLoginRateLimiter();

var app = builder.Build();

// ---- Seed admin (if none) ----
await AdminSeeder.SeedAsync(app.Services, app.Configuration, app.Logger);

// ---- Pipeline ----

// ---- Security headers ----
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    ctx.Response.Headers.Append("X-Frame-Options", "DENY");
    ctx.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    ctx.Response.Headers.Append("Permissions-Policy", "geolocation=(), camera=(), microphone=()");
    ctx.Response.Headers.Append("Content-Security-Policy",
        "default-src 'self'; " +
        "img-src 'self' data:; " +
        "style-src 'self' 'unsafe-inline'; " +
        "script-src 'self' 'unsafe-inline' https://js.stripe.com; " +
        "frame-src https://js.stripe.com; " +
        "connect-src 'self' https://api.stripe.com; " +
        "form-action 'self' https://checkout.stripe.com; " +
        "base-uri 'self'; " +
        "object-src 'none';");
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// ==================================================
// PUBLIC STOREFRONT
// ==================================================

app.MapGet("/", StorefrontEndpoints.HomeAsync);
app.MapGet("/products", StorefrontEndpoints.CatalogAsync);
app.MapGet("/products/{slug}", StorefrontEndpoints.DetailAsync);
app.MapGet("/search", StorefrontEndpoints.SearchAsync);

// ---- Cart ----
app.MapGet("/cart", CartEndpoints.PageAsync);
app.MapGet("/cart/fragment", CartEndpoints.FragmentAsync);
app.MapGet("/cart/badge", CartEndpoints.BadgeAsync);
app.MapPost("/cart/items", CartEndpoints.AddAsync);
app.MapPost("/cart/items/{id:long}/quantity", CartEndpoints.UpdateQuantityAsync);
app.MapPost("/cart/items/{id:long}/delete", CartEndpoints.DeleteAsync);

// ---- Checkout & Webhook ----
app.MapGet("/checkout", CheckoutEndpoints.FormAsync);
app.MapPost("/checkout", CheckoutEndpoints.SubmitAsync);
app.MapGet("/checkout/success", CheckoutEndpoints.SuccessAsync);
app.MapGet("/checkout/cancelled", CheckoutEndpoints.CancelledAsync);
app.MapPost("/webhooks/stripe", CheckoutEndpoints.StripeWebhookAsync).DisableAntiforgery();

// ==================================================
// ADMIN AUTH ENDPOINTS (not behind AdminOnly)
// ==================================================

app.MapGet("/admin/login", (HttpContext ctx) =>
{
    var html = LoginEndpoints.LoginForm();
    return Render.Page(ctx, html, "Admin Login");
});

app.MapPost("/admin/login", LoginEndpoints.LoginSubmitAsync)
   .RequireRateLimiting(RateLimitSetup.LoginPolicy);

app.MapPost("/admin/logout", async (HttpContext ctx, AuditLogger audit) =>
{
    var idStr = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    long? id = long.TryParse(idStr, out var parsed) ? parsed : null;
    await audit.LogAsync(id, "logout");
    await ctx.SignOutAsync(AuthSetup.AdminScheme);
    return Results.Redirect("/admin/login");
}).RequireAuthorization(AuthSetup.AdminPolicy);

// ==================================================
// ADMIN DASHBOARD
// ==================================================

app.MapGet("/admin", DashboardEndpoints.IndexAsync)
   .RequireAuthorization(AuthSetup.AdminPolicy);

// ---- Category CRUD ----
app.MapGet("/admin/categories", CategoryEndpoints.ListAsync)
   .RequireAuthorization(AuthSetup.AdminPolicy);

app.MapGet("/admin/categories/new", (HttpContext ctx, AppDbContext db) => CategoryEndpoints.FormAsync(ctx, db, null))
   .RequireAuthorization(AuthSetup.AdminPolicy);

app.MapGet("/admin/categories/{id:long}/edit", (HttpContext ctx, AppDbContext db, long id) => CategoryEndpoints.FormAsync(ctx, db, id))
   .RequireAuthorization(AuthSetup.AdminPolicy);

app.MapPost("/admin/categories", CategoryEndpoints.CreateAsync)
   .RequireAuthorization(AuthSetup.AdminPolicy);

app.MapPost("/admin/categories/{id:long}", CategoryEndpoints.UpdateAsync)
   .RequireAuthorization(AuthSetup.AdminPolicy);

app.MapPost("/admin/categories/{id:long}/delete", CategoryEndpoints.DeleteAsync)
   .RequireAuthorization(AuthSetup.AdminPolicy);

// ---- Product CRUD ----
app.MapGet("/admin/products", ProductEndpoints.ListAsync)
   .RequireAuthorization(AuthSetup.AdminPolicy);

app.MapGet("/admin/products/new", (HttpContext ctx, AppDbContext db) => ProductEndpoints.FormAsync(ctx, db, null, null))
   .RequireAuthorization(AuthSetup.AdminPolicy);

app.MapGet("/admin/products/{id:long}/edit", (HttpContext ctx, AppDbContext db, long id) => ProductEndpoints.FormAsync(ctx, db, id, null))
   .RequireAuthorization(AuthSetup.AdminPolicy);

app.MapPost("/admin/products", ProductEndpoints.CreateAsync)
   .RequireAuthorization(AuthSetup.AdminPolicy);

app.MapPost("/admin/products/{id:long}", ProductEndpoints.UpdateAsync)
   .RequireAuthorization(AuthSetup.AdminPolicy);

app.MapPost("/admin/products/{id:long}/delete", ProductEndpoints.DeleteAsync)
   .RequireAuthorization(AuthSetup.AdminPolicy);

// ---- Product images ----
app.MapGet("/admin/products/{id:long}/images", ProductImageEndpoints.PageAsync)
   .RequireAuthorization(AuthSetup.AdminPolicy);

app.MapGet("/admin/products/{id:long}/images/fragment", ProductImageEndpoints.FragmentAsync)
   .RequireAuthorization(AuthSetup.AdminPolicy);

app.MapPost("/admin/products/{id:long}/images", ProductImageEndpoints.UploadAsync)
   .RequireAuthorization(AuthSetup.AdminPolicy);

app.MapPost("/admin/products/{id:long}/images/{imageId:long}/primary", ProductImageEndpoints.SetPrimaryAsync)
   .RequireAuthorization(AuthSetup.AdminPolicy);

app.MapPost("/admin/products/{id:long}/images/{imageId:long}/delete", ProductImageEndpoints.DeleteAsync)
   .RequireAuthorization(AuthSetup.AdminPolicy);

// ---- Orders ----
app.MapGet("/admin/orders", OrderEndpoints.ListAsync)
   .RequireAuthorization(AuthSetup.AdminPolicy);

app.MapGet("/admin/orders/{id:long}", OrderEndpoints.DetailAsync)
   .RequireAuthorization(AuthSetup.AdminPolicy);

app.MapPost("/admin/orders/{id:long}/status", OrderEndpoints.UpdateStatusAsync)
   .RequireAuthorization(AuthSetup.AdminPolicy);

app.MapGet("/_ping", async (AppDbContext db) =>
{
    var count = await db.Categories.CountAsync();
    return Results.Json(new { ok = true, categories = count });
});

app.Run();