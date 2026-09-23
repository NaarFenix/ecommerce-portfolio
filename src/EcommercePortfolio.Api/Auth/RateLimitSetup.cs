using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace EcommercePortfolio.Api.Auth;

public static class RateLimitSetup
{
    public const string LoginPolicy = "login";

    public static IServiceCollection AddLoginRateLimiter(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter(LoginPolicy, opt =>
            {
                opt.Window      = TimeSpan.FromMinutes(1);
                opt.PermitLimit = 5;
                opt.QueueLimit  = 0;
            });

            options.RejectionStatusCode = 429;
        });

        return services;
    }
}
