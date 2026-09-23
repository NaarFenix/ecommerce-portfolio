namespace EcommercePortfolio.Api.Html;

public static class StoreLayout
{
    public static string Shell(string activePath, string content) => $@"
<header class=""store-header"">
  <div class=""store-header-inner"">
    <a href=""/"" class=""store-brand"">ATELIER</a>

    <nav class=""store-nav"">
      <a href=""/"" class=""{(activePath == "/" ? "active" : "")}"">Home</a>
      <a href=""/products"" class=""{(activePath == "/products" ? "active" : "")}"">Shop</a>
      <a href=""/search"" class=""{(activePath == "/search" ? "active" : "")}"">Search</a>
    </nav>

    <div class=""store-actions"">
      <a href=""/cart"" class=""store-cart"" aria-label=""Cart"">
        <svg viewBox=""0 0 16 16"" width=""20"" height=""20"" fill=""currentColor"" aria-hidden=""true"">
          <path d=""M.5 1a.5.5 0 0 0 0 1h1.11l.401 1.607 1.498 7.985A.5.5 0 0 0 4 12h1a2 2 0 1 0 0 4 2 2 0 0 0 0-4h7a2 2 0 1 0 0 4 2 2 0 0 0 0-4h1a.5.5 0 0 0 .491-.408l1.5-8A.5.5 0 0 0 14.5 3H2.89l-.405-1.621A.5.5 0 0 0 2 1H.5zm3.915 10L3.102 4h10.796l-1.313 7h-8.17zM6 14a1 1 0 1 1-2 0 1 1 0 0 1 2 0zm7 0a1 1 0 1 1-2 0 1 1 0 0 1 2 0z""/>
        </svg>
        <span id=""cart-badge""
              hx-get=""/cart/badge""
              hx-trigger=""load, cart-updated from:body""
              hx-swap=""outerHTML""></span>
      </a>
    </div>
  </div>
</header>

<main class=""store-main"">
{content}
</main>

<footer class=""store-footer"">
  <div class=""store-footer-inner"">
    <span>© {System.DateTime.UtcNow.Year} Atelier</span>
    <span class=""store-muted"">Portfolio demo · Payments in Stripe test mode</span>
  </div>
</footer>";
}