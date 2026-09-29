using System.Security.Claims;
using Plataforma.Identidade;

namespace Plataforma.Acesso;

/// <summary>
/// O "porteiro" do site: roda ANTES de entregar qualquer arquivo e confere a regra do caminho.
///   livre     → entrega
///   conta     → precisa estar logado
///   pago      → precisa estar logado E ter a liberação no plano
///   equipe    → precisa ter papel de equipe
///   bloqueado / sem regra → 404
/// Como a conferência é no servidor, desligar JavaScript ou usar F12 não adianta.
/// </summary>
public sealed class PortaoDoSite(RequestDelegate proximo)
{
    public const string ChaveNivel = "nivel-conteudo";

    public async Task InvokeAsync(HttpContext ctx, RegrasConteudo regras, ServicoAcesso acesso)
    {
        var caminho = ctx.Request.Path.Value ?? "/";
        if (caminho.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || caminho.Equals("/api", StringComparison.OrdinalIgnoreCase))
        {
            await proximo(ctx);
            return;
        }

        // "/" e pastas viram index.html
        var arquivo = caminho.EndsWith('/') ? caminho + "index.html" : caminho;
        var regra = await regras.Encontrar(caminho) ?? (arquivo != caminho ? await regras.Encontrar(arquivo) : null);

        if (regra is null || regra.Nivel == "bloqueado")
        {
            await NaoEncontrado(ctx);
            return;
        }

        var logado = ctx.User.Identity?.IsAuthenticated == true;
        var volta = Uri.EscapeDataString(caminho + ctx.Request.QueryString);

        switch (regra.Nivel)
        {
            case "conta" when !logado:
            case "pago" when !logado:
            case "equipe" when !logado:
                ctx.Response.Redirect($"/entrar.html?volta={volta}");
                return;

            case "equipe" when !Permissoes.EhEquipe(ctx.User):
                await NaoEncontrado(ctx);
                return;

            case "pago":
                var id = Guid.Parse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                if (!(await acesso.Liberacoes(id)).Contains(regra.Liberacao!))
                {
                    ctx.Response.Redirect($"/planos.html?bloqueado={volta}&precisa={Uri.EscapeDataString(regra.Liberacao!)}");
                    return;
                }
                break;
        }

        ctx.Items[ChaveNivel] = regra.Nivel;
        if (arquivo != caminho)
        {
            ctx.Request.Path = arquivo;
            ctx.SetEndpoint(null); // "/" tinha caído na rota de "não encontrado"; agora é o index.html
        }
        await proximo(ctx);
    }

    /// <summary>Cabeçalhos de cache: conteúdo com login nunca fica guardado em cache compartilhado.</summary>
    public static void AjustarCabecalhos(HttpContext ctx)
    {
        var nivel = ctx.Items[ChaveNivel] as string ?? "livre";
        var h = ctx.Response.Headers;
        if (nivel != "livre")
        {
            h.CacheControl = "private, no-store";
            h["X-Robots-Tag"] = "noindex";
        }
        else if (ctx.Request.Path.Value!.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            h.CacheControl = "no-cache";
        else
            h.CacheControl = "public, max-age=3600";
    }

    static async Task NaoEncontrado(HttpContext ctx)
    {
        ctx.Response.StatusCode = 404;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.WriteAsync("""
            <!DOCTYPE html><html lang="pt-BR"><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Página não encontrada</title><link rel="stylesheet" href="/css/estilo.css"></head>
            <body><main class="container" style="padding:4rem 1rem"><h1>Página não encontrada</h1>
            <p>O endereço pode ter mudado. <a href="/">Voltar para o início</a></p></main></body></html>
            """);
    }
}
