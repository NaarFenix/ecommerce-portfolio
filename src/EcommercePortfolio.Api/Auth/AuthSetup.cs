using Microsoft.AspNetCore.Authentication.Cookies;

namespace EcommercePortfolio.Api.Auth;

public static class AuthSetup
{
    public const string AdminScheme = "AdminCookie";
    public const string AdminPolicy = "AdminOnly";

    public static IServiceCollection AddAdminAuth(this IServiceCollection services)
    {
        services
            .AddAuthentication(AdminScheme)
            .AddCookie(AdminScheme, options =>
            {
                options.Cookie.Name         = "__Host-admin-auth";
                options.Cookie.HttpOnly     = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite     = SameSiteMode.Strict;
                options.Cookie.Path         = "/";
                options.ExpireTimeSpan      = TimeSpan.FromHours(2);
                options.SlidingExpiration   = true;
                options.LoginPath           = "/admin/login";
                options.AccessDeniedPath    = "/admin/login";
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AdminPolicy, p =>
                p.RequireAuthenticatedUser()
                 .RequireClaim(System.Security.Claims.ClaimTypes.Role, "Admin", "SuperAdmin"));
        });

        return services;
    }
}
