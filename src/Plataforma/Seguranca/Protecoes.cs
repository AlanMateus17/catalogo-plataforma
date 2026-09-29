using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Plataforma.Seguranca;

/// <summary>
/// Cabeçalhos que mandam o navegador se proteger:
/// - CSP: só roda script do próprio site (bloqueia script injetado);
/// - frame-ancestors: ninguém coloca o site dentro de um iframe para enganar cliques;
/// - nosniff, Referrer-Policy, Permissions-Policy.
/// </summary>
public sealed class CabecalhosSeguranca(RequestDelegate proximo)
{
    const string Csp =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data: https:; " +
        "media-src 'self'; " +
        "frame-src https://www.youtube.com https://www.youtube-nocookie.com https://maps.google.com https://www.google.com; " +
        "connect-src 'self'; " +
        "form-action 'self' https://formspree.io; " +
        "frame-ancestors 'self'; base-uri 'self'; object-src 'none'";

    public Task InvokeAsync(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        h.ContentSecurityPolicy = Csp;
        h.XContentTypeOptions = "nosniff";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        // Demos precisam abrir dentro do iframe do próprio site
        h.XFrameOptions = "SAMEORIGIN";
        return proximo(ctx);
    }
}

/// <summary>
/// Proteção contra CSRF: todo POST/PUT/DELETE da API precisa trazer o cabeçalho X-CSRF
/// com o token que a própria página pediu em /api/conta/csrf. Um site malicioso
/// não consegue ler esse token, então não consegue agir em nome do aluno.
/// Os webhooks ficam de fora: eles não usam cookie, usam assinatura.
/// </summary>
public sealed class ValidarCsrfAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext ctx)
    {
        var metodo = ctx.HttpContext.Request.Method;
        if (HttpMethods.IsGet(metodo) || HttpMethods.IsHead(metodo) || HttpMethods.IsOptions(metodo)) return;
        if (ctx.Filters.OfType<SemCsrfAttribute>().Any()) return;

        var af = ctx.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
        try { await af.ValidateRequestAsync(ctx.HttpContext); }
        catch (AntiforgeryValidationException)
        {
            ctx.Result = new ObjectResult(new { erro = "Sessão da página expirou. Recarregue e tente de novo.", codigo = "csrf" }) { StatusCode = 400 };
        }
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SemCsrfAttribute : Attribute, IFilterMetadata;
