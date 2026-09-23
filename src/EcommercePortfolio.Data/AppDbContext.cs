using EcommercePortfolio.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EcommercePortfolio.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // ---------- Categories ----------
        b.Entity<Category>(e =>
        {
            e.HasIndex(c => c.Slug).IsUnique();
            e.Property(c => c.Name).HasMaxLength(120).IsRequired();
            e.Property(c => c.Slug).HasMaxLength(140).IsRequired();
        });

        // ---------- Products ----------
        b.Entity<Product>(e =>
        {
            e.HasIndex(p => p.Slug).IsUnique();
            e.HasIndex(p => p.Sku).IsUnique();
            e.HasIndex(p => p.CategoryId).HasDatabaseName("idx_products_category");
            e.Property(p => p.Sku).HasMaxLength(64).IsRequired();
            e.Property(p => p.Name).HasMaxLength(200).IsRequired();
            e.Property(p => p.Slug).HasMaxLength(220).IsRequired();
            e.Property(p => p.Price).HasPrecision(10, 2);
            e.Property(p => p.CompareAtPrice).HasPrecision(10, 2);
            e.HasOne(p => p.Category)
             .WithMany(c => c.Products)
             .HasForeignKey(p => p.CategoryId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ---------- ProductImages ----------
        b.Entity<ProductImage>(e =>
        {
            e.HasIndex(i => i.ProductId).HasDatabaseName("idx_product_images_product");
            e.Property(i => i.Url).HasMaxLength(500).IsRequired();
            e.Property(i => i.AltText).HasMaxLength(200).IsRequired();
            e.HasOne(i => i.Product)
             .WithMany(p => p.Images)
             .HasForeignKey(i => i.ProductId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- AdminUsers ----------
        b.Entity<AdminUser>(e =>
        {
            e.HasIndex(a => a.Username).IsUnique();
            e.HasIndex(a => a.Email).IsUnique();
            e.Property(a => a.Username).HasMaxLength(64).IsRequired();
            e.Property(a => a.Email).HasMaxLength(256).IsRequired();
            e.Property(a => a.PasswordHash).HasMaxLength(500).IsRequired();
            e.Property(a => a.TotpSecret).HasMaxLength(200);
            e.Property(a => a.Role).HasMaxLength(32).IsRequired();
            // "2fa" isn't a word boundary the convention understands; pin the column explicitly.
            e.Property(a => a.Is2faEnabled).HasColumnName("is_2fa_enabled");
        });

        // ---------- Customers ----------
        b.Entity<Customer>(e =>
        {
            e.HasIndex(c => c.Email).IsUnique();
            e.Property(c => c.Email).HasMaxLength(256).IsRequired();
            e.Property(c => c.PasswordHash).HasMaxLength(500);
            e.Property(c => c.FullName).HasMaxLength(200).IsRequired();
            e.Property(c => c.Phone).HasMaxLength(32);
        });

        // ---------- Carts ----------
        b.Entity<Cart>(e =>
        {
            e.HasIndex(c => c.SessionToken).IsUnique().HasDatabaseName("idx_carts_session_token");
            e.HasIndex(c => c.CustomerId).HasDatabaseName("idx_carts_customer");
            e.HasOne(c => c.Customer)
             .WithMany()
             .HasForeignKey(c => c.CustomerId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<CartItem>(e =>
        {
            e.HasIndex(ci => new { ci.CartId, ci.ProductId }).IsUnique();
            e.HasOne(ci => ci.Cart)
             .WithMany(c => c.Items)
             .HasForeignKey(ci => ci.CartId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ci => ci.Product)
             .WithMany()
             .HasForeignKey(ci => ci.ProductId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- Orders ----------
        b.Entity<Order>(e =>
        {
            e.HasIndex(o => o.OrderNumber).IsUnique();
            e.HasIndex(o => o.Status).HasDatabaseName("idx_orders_status");
            e.HasIndex(o => o.CustomerId).HasDatabaseName("idx_orders_customer");
            e.Property(o => o.OrderNumber).HasMaxLength(20).IsRequired();
            e.Property(o => o.Email).HasMaxLength(256).IsRequired();
            e.Property(o => o.FullName).HasMaxLength(200).IsRequired();
            e.Property(o => o.Phone).HasMaxLength(32);
            e.Property(o => o.ShippingAddress).HasMaxLength(300).IsRequired();
            e.Property(o => o.City).HasMaxLength(120).IsRequired();
            e.Property(o => o.PostalCode).HasMaxLength(20);
            e.Property(o => o.Country).HasMaxLength(80).IsRequired();
            e.Property(o => o.Subtotal).HasPrecision(10, 2);
            e.Property(o => o.ShippingFee).HasPrecision(10, 2);
            e.Property(o => o.Total).HasPrecision(10, 2);
            e.Property(o => o.Status).HasMaxLength(20).IsRequired();
            e.HasOne(o => o.Customer)
             .WithMany()
             .HasForeignKey(o => o.CustomerId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<OrderItem>(e =>
        {
            e.HasIndex(oi => oi.OrderId).HasDatabaseName("idx_order_items_order");
            e.Property(oi => oi.ProductNameSnapshot).HasMaxLength(200).IsRequired();
            e.Property(oi => oi.UnitPriceSnapshot).HasPrecision(10, 2);
            e.Property(oi => oi.LineTotal).HasPrecision(10, 2);
            e.HasOne(oi => oi.Order)
             .WithMany(o => o.Items)
             .HasForeignKey(oi => oi.OrderId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(oi => oi.Product)
             .WithMany()
             .HasForeignKey(oi => oi.ProductId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // ---------- Payments ----------
        b.Entity<Payment>(e =>
        {
            e.HasIndex(p => new { p.Provider, p.ProviderPaymentId })
             .IsUnique()
             .HasDatabaseName("idx_payments_provider_id");
            e.HasIndex(p => p.OrderId).HasDatabaseName("idx_payments_order");
            e.Property(p => p.Provider).HasMaxLength(30).IsRequired();
            e.Property(p => p.ProviderPaymentId).HasMaxLength(200).IsRequired();
            e.Property(p => p.Amount).HasPrecision(10, 2);
            e.Property(p => p.Currency).HasMaxLength(3).IsRequired();
            e.Property(p => p.Status).HasMaxLength(20).IsRequired();
            e.Property(p => p.RawResponseJson).HasColumnType("jsonb");
            e.HasOne(p => p.Order)
             .WithMany(o => o.Payments)
             .HasForeignKey(p => p.OrderId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- AuditLogs ----------
        b.Entity<AuditLog>(e =>
        {
            e.HasIndex(a => a.AdminUserId).HasDatabaseName("idx_audit_logs_admin");
            e.HasIndex(a => a.CreatedAt).HasDatabaseName("idx_audit_logs_created");
            e.Property(a => a.Action).HasMaxLength(80).IsRequired();
            e.Property(a => a.EntityType).HasMaxLength(60);
            e.Property(a => a.IpAddress).HasMaxLength(64);
            e.Property(a => a.DetailsJson).HasColumnType("jsonb");
            e.HasOne(a => a.AdminUser)
             .WithMany()
             .HasForeignKey(a => a.AdminUserId)
             .OnDelete(DeleteBehavior.SetNull);
        });
    }
}