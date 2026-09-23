using EcommercePortfolio.Api.Auth;
using EcommercePortfolio.Api.Html;
using EcommercePortfolio.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;

namespace EcommercePortfolio.Api.Endpoints;

public static class LoginEndpoints
{
    public static string LoginForm(string? error = null, string? username = null) =>
$@"<h1>Admin Login</h1>
{(error is null ? "" : $"<p class='error'>{Layout.H(error)}</p>")}
<form method=""post"" action=""/admin/login"">
  <div>
    <label for=""username"">Username or email</label>
    <input id=""username"" name=""username"" type=""text"" autocomplete=""username"" required value=""{Layout.H(username)}"">
  </div>
  <div>
    <label for=""password"">Password</label>
    <input id=""password"" name=""password"" type=""password"" autocomplete=""current-password"" required>
  </div>
  <button type=""submit"">Log in</button>
</form>";

    public static async Task<IResult> LoginSubmitAsync(
        HttpContext ctx,
        AdminAuthService auth,
        AuditLogger audit,
        ILogger<AdminAuthService> logger)
    {
        var form = await ctx.Request.ReadFormAsync();
        var username = form["username"].ToString().Trim();
        var password = form["password"].ToString();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return Render.Page(ctx, LoginForm("Username and password required.", username), "Admin Login");

        var (result, user) = await auth.VerifyAsync(username, password);

        if (!result.Success)
        {
            logger.LogWarning("Failed admin login for '{Username}' from {Ip}", username, ctx.Connection.RemoteIpAddress);
            await audit.LogAsync(user?.Id, "login.failed", details: new { username, reason = result.Reason });
            return Render.Page(ctx, LoginForm(result.Reason, username), "Admin Login");
        }

        // Build claims
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user!.Id.ToString()),
            new(ClaimTypes.Name,           user.Username),
            new(ClaimTypes.Email,          user.Email),
            new(ClaimTypes.Role,           user.Role)
        };
        var identity = new ClaimsIdentity(claims, AuthSetup.AdminScheme);
        await ctx.SignInAsync(AuthSetup.AdminScheme, new ClaimsPrincipal(identity), new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc   = DateTimeOffset.UtcNow.AddHours(2)
        });

        await audit.LogAsync(user.Id, "login.success", details: new { username = user.Username });
        logger.LogInformation("Admin '{Username}' logged in from {Ip}", user.Username, ctx.Connection.RemoteIpAddress);
        return Results.Redirect("/admin");
    }
}