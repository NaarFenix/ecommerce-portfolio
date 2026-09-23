namespace EcommercePortfolio.Api.Html;

public static class AdminLayout
{
    public static string Shell(string activePath, string content) => $@"
<header class=""admin-header"">
  <h1>E-Commerce Admin</h1>
  <nav>
    <a href=""/admin"" class=""{(activePath == "/admin" ? "active" : "")}"">Dashboard</a>
    <a href=""/admin/products"" class=""{(activePath == "/admin/products" ? "active" : "")}"">Products</a>
    <a href=""/admin/categories"" class=""{(activePath == "/admin/categories" ? "active" : "")}"">Categories</a>
    <a href=""/admin/orders"" class=""{(activePath == "/admin/orders" ? "active" : "")}"">Orders</a>
    <form method=""post"" action=""/admin/logout"" style=""display:inline"">
      <button type=""submit"" class=""link"">Log out</button>
    </form>
  </nav>
</header>
<main>
{content}
</main>";
}