using Microsoft.AspNetCore.Antiforgery;

namespace EcommercePortfolio.Api.Html;

public static class Render
{
    private static string? GetToken(HttpContext ctx)
    {
        var af = ctx.RequestServices.GetRequiredService<IAntiforgery>();
        try { return af.GetAndStoreTokens(ctx).RequestToken; }
        catch { return null; }
    }

    public static IResult StorePage(HttpContext ctx, string activePath, string fragmentHtml, string title)
    {
        if (ctx.Request.Headers.ContainsKey("HX-Request"))
            return Results.Content(fragmentHtml, "text/html; charset=utf-8");

        var token = GetToken(ctx);
        return Results.Content(
            Layout.Wrap(title, StoreLayout.Shell(activePath, fragmentHtml), "store", token),
            "text/html; charset=utf-8");
    }

    public static IResult AdminPage(HttpContext ctx, string activePath, string fragmentHtml, string title)
    {
        if (ctx.Request.Headers.ContainsKey("HX-Request"))
            return Results.Content(fragmentHtml, "text/html; charset=utf-8");

        var token = GetToken(ctx);
        return Results.Content(
            Layout.Wrap(title, AdminLayout.Shell(activePath, fragmentHtml), "admin", token),
            "text/html; charset=utf-8");
    }

    public static IResult Page(HttpContext ctx, string fragmentHtml, string title)
    {
        if (ctx.Request.Headers.ContainsKey("HX-Request"))
            return Results.Content(fragmentHtml, "text/html; charset=utf-8");

        var token = GetToken(ctx);
        return Results.Content(
            Layout.Wrap(title, fragmentHtml, null, token),
            "text/html; charset=utf-8");
    }
}