using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Plataforma.Identidade;

/// <summary>
/// O que cada papel pode fazer. Os papéis ficam no banco (usuario_papeis);
/// as permissões de cada papel ficam AQUI, no código, para ninguém conseguir
/// se dar poderes mexendo só no banco ou numa tela.
/// </summary>
public static class Permissoes
{
    public const string PainelVer = "painel.ver";
    public const string UsuariosVer = "usuarios.ver";
    public const string UsuariosEditar = "usuarios.editar";         // bloquear, confirmar e-mail, encerrar sessões
    public const string PapeisGerenciar = "papeis.gerenciar";
    public const string AcessosGerenciar = "acessos.gerenciar";     // conceder e revogar acesso manual
    public const string PedidosVer = "pedidos.ver";
    public const string PedidosGerenciar = "pedidos.gerenciar";     // venda manual, reembolso
    public const string ProdutosGerenciar = "produtos.gerenciar";
    public const string CuponsGerenciar = "cupons.gerenciar";
    public const string CodigosGerenciar = "codigos.gerenciar";
    public const string ConteudoGerenciar = "conteudo.gerenciar";   // regras livre/conta/pago
    public const string WebhooksVer = "webhooks.ver";
    public const string WebhooksReprocessar = "webhooks.reprocessar";
    public const string AuditoriaVer = "auditoria.ver";

    public static readonly string[] Todas =
    [
        PainelVer, UsuariosVer, UsuariosEditar, PapeisGerenciar, AcessosGerenciar, PedidosVer, PedidosGerenciar,
        ProdutosGerenciar, CuponsGerenciar, CodigosGerenciar, ConteudoGerenciar, WebhooksVer, WebhooksReprocessar, AuditoriaVer,
    ];

    public static readonly IReadOnlyDictionary<string, string[]> PorPapel = new Dictionary<string, string[]>
    {
        ["aluno"] = [],
        ["suporte"] = [PainelVer, UsuariosVer, UsuariosEditar, AcessosGerenciar, PedidosVer, WebhooksVer],
        ["financeiro"] = [PainelVer, UsuariosVer, PedidosVer, PedidosGerenciar, CuponsGerenciar, CodigosGerenciar, WebhooksVer, WebhooksReprocessar],
        ["professor"] = [PainelVer, UsuariosVer, ConteudoGerenciar],
        ["admin"] = Todas,
    };

    /// <summary>Papéis que contam como "equipe" (entram no painel).</summary>
    public static readonly string[] PapeisEquipe = ["suporte", "financeiro", "professor", "admin"];

    public static HashSet<string> DoUsuario(ClaimsPrincipal u) =>
        u.FindAll(ClaimTypes.Role)
         .SelectMany(c => PorPapel.TryGetValue(c.Value, out var p) ? p : [])
         .ToHashSet();

    public static bool EhEquipe(ClaimsPrincipal u) => u.FindAll(ClaimTypes.Role).Any(c => PapeisEquipe.Contains(c.Value));

    /// <summary>Cria uma política "perm:X" para cada permissão.</summary>
    public static void Registrar(AuthorizationOptions opcoes)
    {
        foreach (var p in Todas)
            opcoes.AddPolicy("perm:" + p, pol => pol.RequireAuthenticatedUser().AddRequirements(new RequisitoPermissao(p)));
    }
}

public sealed record RequisitoPermissao(string Permissao) : IAuthorizationRequirement;

/// <summary>
/// Confere a permissão e, para a equipe, exige verificação em duas etapas ativa
/// (se a configuração Seguranca:Exigir2faEquipe estiver ligada).
/// </summary>
public sealed class VerificadorPermissao(IConfiguration config) : AuthorizationHandler<RequisitoPermissao>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext ctx, RequisitoPermissao req)
    {
        if (!Permissoes.DoUsuario(ctx.User).Contains(req.Permissao)) return Task.CompletedTask;
        var exige2fa = config.GetValue("Seguranca:Exigir2faEquipe", true);
        if (exige2fa && ctx.User.FindFirst("dois_fatores")?.Value != "sim") return Task.CompletedTask;
        ctx.Succeed(req);
        return Task.CompletedTask;
    }
}
