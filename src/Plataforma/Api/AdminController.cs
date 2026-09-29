using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Acesso;
using Plataforma.Auditoria;
using Plataforma.Dados;
using Plataforma.Email;
using Plataforma.Identidade;
using Plataforma.Seguranca;
using Plataforma.Vendas;
using P = Plataforma.Identidade.Permissoes;

namespace Plataforma.Api;

/// <summary>
/// Painel do administrador. Cada ação exige uma permissão (veja Identidade/Permissoes.cs)
/// e fica registrada na auditoria. A equipe precisa ter verificação em duas etapas ativa.
/// </summary>
[ApiController, Route("api/admin"), ValidarCsrf]
public sealed class AdminController(
    Banco banco, UserManager<Usuario> usuarios, ServicoAcesso acesso, ServicoVendas vendas, ServicoContas contas,
    RegistroAuditoria auditoria, RegrasConteudo regras, ProcessadorWebhooks webhooks, IEnumerable<IPlataformaVenda> plataformas,
    IEnviadorEmail enviador, ModelosEmail modelos, IConfiguration config) : ControllerBase
{
    const int PorPagina = 50;
    Guid MeuId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    static ObjectResult Erro(string msg, int status = 400) => new(new { erro = msg }) { StatusCode = status };
    static int Offset(int pagina) => (Math.Max(pagina, 1) - 1) * PorPagina;

    // ================================================================ Painel
    [HttpGet("painel"), Authorize(Policy = "perm:" + P.PainelVer)]
    public async Task<IActionResult> Painel()
    {
        const string Mes = "date_trunc('month', now() AT TIME ZONE 'America/Sao_Paulo') AT TIME ZONE 'America/Sao_Paulo'";
        var podeVerDinheiro = P.DoUsuario(User).Contains(P.PedidosGerenciar); // suporte vê pedidos de um aluno, mas não o faturamento
        return Ok(new
        {
            usuarios = await banco.Escalar<long>("SELECT count(*) FROM usuarios WHERE excluido_em IS NULL"),
            novos7 = await banco.Escalar<long>("SELECT count(*) FROM usuarios WHERE excluido_em IS NULL AND criado_em > now() - interval '7 days'"),
            novos30 = await banco.Escalar<long>("SELECT count(*) FROM usuarios WHERE excluido_em IS NULL AND criado_em > now() - interval '30 days'"),
            naoConfirmados = await banco.Escalar<long>("SELECT count(*) FROM usuarios WHERE excluido_em IS NULL AND NOT email_confirmado"),
            comAcessoPago = await banco.Escalar<long>("""
                SELECT count(DISTINCT a.usuario_id) FROM acessos a WHERE a.status='ativo' AND a.origem <> 'teste'
                AND a.inicio <= now() AND (a.fim IS NULL OR a.fim > now()) AND a.usuario_id IS NOT NULL
                """),
            testesAtivos = await banco.Escalar<long>("SELECT count(*) FROM acessos WHERE origem='teste' AND status='ativo' AND fim > now()"),
            testesTotal = await banco.Escalar<long>("SELECT count(*) FROM usuarios WHERE teste_usado_em IS NOT NULL"),
            testesQueCompraram = await banco.Escalar<long>("""
                SELECT count(*) FROM usuarios u WHERE u.teste_usado_em IS NOT NULL AND EXISTS (
                  SELECT 1 FROM pedidos p WHERE p.usuario_id = u.id AND p.status='aprovado' AND p.valor_centavos > 0 AND p.criado_em > u.teste_usado_em)
                """),
            reservadosSemConta = await banco.Escalar<long>("SELECT count(*) FROM acessos WHERE usuario_id IS NULL AND status='ativo'"),
            webhooksComErro = await banco.Escalar<long>("SELECT count(*) FROM webhook_eventos WHERE assinatura_valida AND NOT processado"),
            receitaMes = !podeVerDinheiro ? null : await banco.Lista($"""
                SELECT plataforma, count(*) AS vendas, coalesce(sum(valor_centavos),0)::bigint AS total FROM pedidos
                WHERE status='aprovado' AND valor_centavos > 0 AND criado_em >= {Mes} GROUP BY plataforma ORDER BY total DESC
                """, r => new { plataforma = r.Texto("plataforma"), vendas = r.Longo("vendas"), total = r.Longo("total") }),
            reembolsosMes = !podeVerDinheiro ? null : await banco.Escalar<long?>($"""
                SELECT coalesce(sum(valor_centavos),0)::bigint FROM pedidos WHERE status IN ('reembolsado','chargeback') AND atualizado_em >= {Mes}
                """),
            receita12Meses = !podeVerDinheiro ? null : await banco.Lista("""
                SELECT to_char(date_trunc('month', criado_em AT TIME ZONE 'America/Sao_Paulo'), 'YYYY-MM') AS mes,
                       coalesce(sum(valor_centavos),0)::bigint AS total
                FROM pedidos WHERE status='aprovado' AND criado_em > now() - interval '12 months'
                GROUP BY 1 ORDER BY 1
                """, r => new { mes = r.Texto("mes"), total = r.Longo("total") }),
            ultimosPedidos = !podeVerDinheiro ? null : await banco.Lista("""
                SELECT p.id, p.email_comprador, pr.nome, p.plataforma, p.status, p.valor_centavos, p.criado_em
                FROM pedidos p JOIN produtos pr ON pr.id=p.produto_id WHERE p.plataforma <> 'teste' ORDER BY p.criado_em DESC LIMIT 10
                """, LerPedido),
        });
    }

    // ================================================================ Usuários
    [HttpGet("usuarios"), Authorize(Policy = "perm:" + P.UsuariosVer)]
    public async Task<IActionResult> Usuarios(string? busca, string? papel, int pagina = 1)
    {
        var filtro = $"%{(busca ?? "").Trim()}%";
        var lista = await banco.Lista($"""
            SELECT u.id, u.nome, u.email, u.email_confirmado, u.bloqueado_por_admin, u.criado_em, u.ultimo_login_em, u.dois_fatores,
                   coalesce((SELECT array_agg(papel_id ORDER BY papel_id) FROM usuario_papeis WHERE usuario_id=u.id), ARRAY[]::text[]) AS papeis,
                   EXISTS (SELECT 1 FROM acessos a WHERE a.usuario_id=u.id AND a.status='ativo' AND (a.fim IS NULL OR a.fim > now())) AS tem_acesso,
                   NOT EXISTS (SELECT 1 FROM acessos a WHERE a.usuario_id=u.id AND a.status='ativo' AND (a.fim IS NULL OR a.fim > now()) AND a.origem <> 'teste')
                     AND EXISTS (SELECT 1 FROM acessos a WHERE a.usuario_id=u.id AND a.status='ativo' AND a.fim > now() AND a.origem = 'teste') AS em_teste,
                   count(*) OVER () AS total
            FROM usuarios u
            WHERE u.excluido_em IS NULL AND (u.nome ILIKE @f OR u.email ILIKE @f)
              AND (@papel::text IS NULL OR EXISTS (SELECT 1 FROM usuario_papeis x WHERE x.usuario_id=u.id AND x.papel_id=@papel))
            ORDER BY u.criado_em DESC LIMIT {PorPagina} OFFSET @o
            """, r => new
        {
            id = r.Id("id"), nome = r.Texto("nome"), email = r.Texto("email"), confirmado = r.Logico("email_confirmado"),
            bloqueado = r.Logico("bloqueado_por_admin"), criadoEm = r.Data("criado_em"), ultimoLogin = r.DataOuNula("ultimo_login_em"),
            doisFatores = r.Logico("dois_fatores"), papeis = r.Textos("papeis"), temAcesso = r.Logico("tem_acesso"), emTeste = r.Logico("em_teste"), total = r.Longo("total"),
        }, ("f", filtro), ("papel", string.IsNullOrWhiteSpace(papel) ? null : papel), ("o", Offset(pagina)));
        return Ok(new { itens = lista, total = lista.FirstOrDefault()?.total ?? 0, porPagina = PorPagina });
    }

    [HttpGet("usuarios/{id:guid}"), Authorize(Policy = "perm:" + P.UsuariosVer)]
    public async Task<IActionResult> Usuario(Guid id)
    {
        var u = await usuarios.FindByIdAsync(id.ToString());
        if (u is null || u.ExcluidoEm is not null) return NotFound();
        return Ok(new
        {
            u.Id, u.Nome, u.Email, confirmado = u.EmailConfirmado, u.CriadoEm, u.UltimoLoginEm, u.DoisFatores,
            bloqueado = u.BloqueadoPorAdmin, u.MotivoBloqueio, bloqueioTentativasAte = u.BloqueadoPorAdmin ? null : u.BloqueioAte,
            u.TesteUsadoEm, u.TermosVersao, u.TermosAceitosEm,
            papeis = await usuarios.GetRolesAsync(u),
            liberacoes = await acesso.Liberacoes(u.Id),
            acessos = await acesso.DoUsuario(u.Id),
            pedidos = await banco.Lista("""
                SELECT p.id, p.email_comprador, pr.nome, p.plataforma, p.status, p.valor_centavos, p.criado_em
                FROM pedidos p JOIN produtos pr ON pr.id=p.produto_id WHERE p.usuario_id=@u ORDER BY p.criado_em DESC
                """, LerPedido, ("u", u.Id)),
            historico = await banco.Lista("SELECT quando, ator, acao, detalhes, ip FROM auditoria WHERE usuario_id=@u OR alvo=@e ORDER BY quando DESC LIMIT 50",
                r => new { quando = r.Data("quando"), ator = r.Texto("ator"), acao = r.Texto("acao"), detalhes = r.TextoOuNulo("detalhes"), ip = r.TextoOuNulo("ip") },
                ("u", u.Id), ("e", u.Email)),
        });
    }

    public sealed record PapeisPedido(string[]? Papeis);

    [HttpPost("usuarios/{id:guid}/papeis"), Authorize(Policy = "perm:" + P.PapeisGerenciar)]
    public async Task<IActionResult> DefinirPapeis(Guid id, PapeisPedido p)
    {
        var u = await usuarios.FindByIdAsync(id.ToString());
        if (u is null || u.ExcluidoEm is not null) return NotFound();
        var novos = (p.Papeis ?? []).Select(x => x.ToLowerInvariant()).Append("aluno").Distinct().ToList();
        if (novos.Any(x => !P.PorPapel.ContainsKey(x))) return Erro("Papel desconhecido.");
        var atuais = await usuarios.GetRolesAsync(u);

        if (atuais.Contains("admin") && !novos.Contains("admin"))
        {
            if (u.Id == MeuId) return Erro("Você não pode tirar o seu próprio papel de administrador.");
            if (await banco.Escalar<long>("SELECT count(*) FROM usuario_papeis WHERE papel_id='admin'") <= 1)
                return Erro("Precisa existir pelo menos um administrador.");
        }
        if (novos.Any(x => P.PapeisEquipe.Contains(x)) && !u.EmailConfirmado)
            return Erro("Só contas com e-mail confirmado podem ser da equipe.");

        foreach (var r in atuais.Except(novos)) await usuarios.RemoveFromRoleAsync(u, r);
        foreach (var r in novos.Except(atuais)) await usuarios.AddToRoleAsync(u, r);
        await usuarios.UpdateSecurityStampAsync(u); // a pessoa recebe os papéis novos no próximo pedido
        await auditoria.Registrar("papeis.alterados", u.Email, new { antes = atuais, depois = novos });
        return Ok(new { ok = true, papeis = novos });
    }

    public sealed record MotivoPedido(string? Motivo);

    [HttpPost("usuarios/{id:guid}/bloquear"), Authorize(Policy = "perm:" + P.UsuariosEditar)]
    public async Task<IActionResult> Bloquear(Guid id, MotivoPedido p)
    {
        if (id == MeuId) return Erro("Você não pode bloquear a si mesmo.");
        if (string.IsNullOrWhiteSpace(p.Motivo)) return Erro("Informe o motivo do bloqueio.");
        var u = await usuarios.FindByIdAsync(id.ToString());
        if (u is null) return NotFound();
        if ((await usuarios.GetRolesAsync(u)).Any(r => P.PapeisEquipe.Contains(r)) && !User.IsInRole("admin"))
            return Erro("Só um administrador pode bloquear alguém da equipe.", 403);
        await contas.Bloquear(u, p.Motivo.Trim());
        await auditoria.Registrar("usuario.bloqueado", u.Email, new { p.Motivo });
        return Ok(new { ok = true });
    }

    [HttpPost("usuarios/{id:guid}/desbloquear"), Authorize(Policy = "perm:" + P.UsuariosEditar)]
    public async Task<IActionResult> Desbloquear(Guid id)
    {
        var u = await usuarios.FindByIdAsync(id.ToString());
        if (u is null || u.ExcluidoEm is not null) return NotFound();
        if (id == MeuId) return Erro("Peça a outra pessoa da equipe para desbloquear você.", 403);
        if ((await usuarios.GetRolesAsync(u)).Any(r => P.PapeisEquipe.Contains(r)) && !User.IsInRole("admin"))
            return Erro("Só um administrador pode desbloquear alguém da equipe.", 403);
        await contas.Desbloquear(u);
        await auditoria.Registrar("usuario.desbloqueado", u.Email);
        return Ok(new { ok = true });
    }

    // Só administrador: confirmar à mão liga à conta as compras feitas com aquele e-mail.
    // A conta fica sem senha; a pessoa cria a dela em "Esqueci minha senha" (o link vai para o e-mail dela).
    [HttpPost("usuarios/{id:guid}/confirmar-email"), Authorize(Policy = "perm:" + P.PapeisGerenciar)]
    public async Task<IActionResult> ConfirmarEmail(Guid id)
    {
        var u = await usuarios.FindByIdAsync(id.ToString());
        if (u is null || u.ExcluidoEm is not null) return NotFound();
        if (id == MeuId) return Erro("Você não pode confirmar o próprio e-mail por aqui.", 403);
        // Tira a senha que estava na conta não confirmada (pode ter sido criada por outra pessoa)
        if (!u.EmailConfirmado && await usuarios.HasPasswordAsync(u)) { await usuarios.RemovePasswordAsync(u); u = (await usuarios.FindByIdAsync(id.ToString()))!; }
        u.EmailConfirmado = true;
        await usuarios.UpdateAsync(u);
        await acesso.VincularPendentes(u.Id, u.EmailNormalizado);
        await auditoria.Registrar("usuario.email-confirmado-manual", u.Email);
        return Ok(new { ok = true });
    }

    [HttpPost("usuarios/{id:guid}/encerrar-sessoes"), Authorize(Policy = "perm:" + P.UsuariosEditar)]
    public async Task<IActionResult> EncerrarSessoes(Guid id)
    {
        var u = await usuarios.FindByIdAsync(id.ToString());
        if (u is null) return NotFound();
        await usuarios.UpdateSecurityStampAsync(u);
        await auditoria.Registrar("usuario.sessoes-encerradas", u.Email);
        return Ok(new { ok = true });
    }

    [HttpPost("usuarios/{id:guid}/desativar-2fa"), Authorize(Policy = "perm:" + P.PapeisGerenciar)]
    public async Task<IActionResult> Desativar2fa(Guid id, MotivoPedido p)
    {
        if (string.IsNullOrWhiteSpace(p.Motivo)) return Erro("Informe o motivo (ex.: perdeu o celular, identidade conferida por vídeo).");
        var u = await usuarios.FindByIdAsync(id.ToString());
        if (u is null) return NotFound();
        await usuarios.SetTwoFactorEnabledAsync(u, false);
        await usuarios.ResetAuthenticatorKeyAsync(u);
        await auditoria.Registrar("usuario.2fa-desativada-pelo-admin", u.Email, new { p.Motivo });
        return Ok(new { ok = true });
    }

    public sealed record ConcederPedido(int ProdutoId, int? Dias, string? Motivo);

    [HttpPost("usuarios/{id:guid}/acessos"), Authorize(Policy = "perm:" + P.AcessosGerenciar)]
    public async Task<IActionResult> Conceder(Guid id, ConcederPedido p)
    {
        if (string.IsNullOrWhiteSpace(p.Motivo)) return Erro("Informe o motivo (ex.: bolsa, cortesia, correção de compra).");
        if (id == MeuId && !User.IsInRole("admin")) return Erro("Só o administrador pode liberar acesso para a própria conta.", 403);
        if (p.Dias is <= 0 or > 3650) return Erro("Dias deve ser entre 1 e 3650 (ou vazio para usar o do produto).");
        var u = await usuarios.FindByIdAsync(id.ToString());
        if (u is null || u.ExcluidoEm is not null) return NotFound();
        if (await vendas.ProdutoPorId(p.ProdutoId) is not { } prod) return Erro("Produto não existe.");
        var (acessoId, fim) = await banco.Transacao((con, tx) =>
            ServicoAcesso.Conceder(con, tx, u.Id, u.EmailNormalizado, prod.Id, null, "manual", p.Dias, p.Motivo.Trim(), MeuId));
        await auditoria.Registrar("acesso.concedido", u.Email, new { produto = prod.Slug, p.Dias, p.Motivo, acessoId });
        var (a, h) = modelos.AcessoLiberado(u.Nome, prod.Nome, fim);
        try { await enviador.Enviar(u.Email, a, h); } catch { /* acesso já foi dado; e-mail é só aviso */ }
        return Ok(new { ok = true, fim });
    }

    [HttpPost("acessos/{id:long}/revogar"), Authorize(Policy = "perm:" + P.AcessosGerenciar)]
    public async Task<IActionResult> Revogar(long id, MotivoPedido p)
    {
        if (string.IsNullOrWhiteSpace(p.Motivo)) return Erro("Informe o motivo.");
        var n = await acesso.Revogar(id, p.Motivo.Trim());
        if (n == 0) return Erro("Acesso não encontrado ou já revogado.");
        await auditoria.Registrar("acesso.revogado", id.ToString(), new { p.Motivo });
        return Ok(new { ok = true });
    }

    // ================================================================ Pedidos
    static object LerPedido(System.Data.Common.DbDataReader r) => new
    {
        id = r.Id("id"), email = r.Texto("email_comprador"), produto = r.Texto("nome"), plataforma = r.Texto("plataforma"),
        status = r.Texto("status"), valor = r.Inteiro("valor_centavos"), criadoEm = r.Data("criado_em"),
    };

    const string SqlPedidos = """
        SELECT p.id, p.email_comprador, pr.nome, p.plataforma, p.status, p.valor_centavos, p.criado_em, p.id_externo, p.observacao,
               count(*) OVER () AS total
        FROM pedidos p JOIN produtos pr ON pr.id=p.produto_id
        WHERE (@st::text IS NULL OR p.status=@st) AND (@pl::text IS NULL OR p.plataforma=@pl)
          AND (p.email_comprador ILIKE @f OR coalesce(p.id_externo,'') ILIKE @f OR coalesce(p.nome_comprador,'') ILIKE @f)
        ORDER BY p.criado_em DESC
        """;

    [HttpGet("pedidos"), Authorize(Policy = "perm:" + P.PedidosVer)]
    public async Task<IActionResult> Pedidos(string? status, string? plataforma, string? busca, int pagina = 1)
    {
        var lista = await banco.Lista(SqlPedidos + $" LIMIT {PorPagina} OFFSET @o", r => new
        {
            id = r.Id("id"), email = r.Texto("email_comprador"), produto = r.Texto("nome"), plataforma = r.Texto("plataforma"),
            status = r.Texto("status"), valor = r.Inteiro("valor_centavos"), criadoEm = r.Data("criado_em"),
            idExterno = r.TextoOuNulo("id_externo"), observacao = r.TextoOuNulo("observacao"), total = r.Longo("total"),
        }, ("st", Vazio(status)), ("pl", Vazio(plataforma)), ("f", $"%{(busca ?? "").Trim()}%"), ("o", Offset(pagina)));
        return Ok(new { itens = lista, total = lista.FirstOrDefault()?.total ?? 0, porPagina = PorPagina });
    }

    [HttpGet("pedidos.csv"), Authorize(Policy = "perm:" + P.PedidosVer)]
    public async Task<IActionResult> PedidosCsv(string? status, string? plataforma, string? busca)
    {
        var linhas = await banco.Lista(SqlPedidos + " LIMIT 50000", r =>
            string.Join(';', r.Data("criado_em").ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM/yyyy HH:mm"), Csv(r.Texto("email_comprador")),
                Csv(r.Texto("nome")), r.Texto("plataforma"), r.Texto("status"), (r.Inteiro("valor_centavos") / 100m).ToString("0.00", new System.Globalization.CultureInfo("pt-BR")),
                Csv(r.TextoOuNulo("id_externo") ?? ""), Csv(r.TextoOuNulo("observacao") ?? "")),
            ("st", Vazio(status)), ("pl", Vazio(plataforma)), ("f", $"%{(busca ?? "").Trim()}%"));
        await auditoria.Registrar("pedidos.exportados", null, new { status, plataforma, busca, linhas = linhas.Count });
        var csv = "data;email;produto;plataforma;status;valor;id_externo;observacao\n" + string.Join('\n', linhas);
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", "pedidos.csv");
    }

    static string? Vazio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
    // Evita que o Excel execute fórmula vinda de dado digitado por alguém (=, +, -, @)
    static string Csv(string s)
    {
        s = s.Replace(";", ",").Replace("\n", " ").Replace("\r", " ");
        return s.Length > 0 && "=+-@".Contains(s[0]) ? "'" + s : s;
    }

    public sealed record VendaManualPedido(string? Email, string? Nome, int ProdutoId, int ValorCentavos, string? Observacao, int? Dias);

    [HttpPost("pedidos/manual"), Authorize(Policy = "perm:" + P.PedidosGerenciar)]
    public async Task<IActionResult> VendaManual(VendaManualPedido p)
    {
        if (!Emails.Valido(p.Email)) return Erro("E-mail inválido.");
        if (p.ValorCentavos < 0) return Erro("Valor inválido.");
        if (string.IsNullOrWhiteSpace(p.Observacao)) return Erro("Informe como foi pago (ex.: Pix em 12/10, comprovante no WhatsApp).");
        if (p.Dias is <= 0 or > 3650) return Erro("Dias deve ser entre 1 e 3650 (ou vazio para usar o do produto).");
        if (await vendas.ProdutoPorId(p.ProdutoId) is null) return Erro("Produto não existe.");
        if (!User.IsInRole("admin") && Emails.Normalizar(p.Email!) == (await usuarios.GetUserAsync(User))!.EmailNormalizado)
            return Erro("Ninguém da equipe registra venda para o próprio e-mail. Peça ao administrador.", 403);
        return Ok(new { ok = true, mensagem = await vendas.VendaManual(MeuId, p.Email!, p.Nome, p.ProdutoId, p.ValorCentavos, p.Observacao.Trim(), p.Dias) });
    }

    [HttpPost("pedidos/{id:guid}/reembolsar"), Authorize(Policy = "perm:" + P.PedidosGerenciar)]
    public async Task<IActionResult> Reembolsar(Guid id, MotivoPedido p)
    {
        if (string.IsNullOrWhiteSpace(p.Motivo)) return Erro("Informe o motivo.");
        return Ok(new { ok = true, mensagem = await vendas.Reembolsar(id, p.Motivo.Trim()) });
    }

    // ================================================================ Produtos
    [HttpGet("produtos"), Authorize(Policy = "perm:" + P.PainelVer)]
    public async Task<IActionResult> Produtos()
    {
        var lista = await banco.Lista($"SELECT {Produto.Colunas} FROM produtos ORDER BY ordem, id", Produto.Ler);
        var externos = await banco.Lista("SELECT plataforma, id_externo, produto_id FROM produtos_externos ORDER BY plataforma",
            r => new { plataforma = r.GetString(0), idExterno = r.GetString(1), produtoId = r.GetInt32(2) });
        return Ok(lista.Select(p => new { produto = p, externos = externos.Where(e => e.produtoId == p.Id) }));
    }

    public sealed record ProdutoPedido(string? Slug, string? Nome, string? Descricao, string? Tipo, int PrecoCentavos, int? PrecoDeCentavos,
        int Parcelas, int? DiasAcesso, string[]? Liberacoes, string[]? Beneficios, string? UrlKiwify, string? UrlHotmart,
        bool MercadoPago, bool Ativo, bool Destaque, int Ordem);

    static string? ValidarProduto(ProdutoPedido p)
    {
        if (string.IsNullOrWhiteSpace(p.Nome)) return "Informe o nome.";
        if (p.Slug is null || !System.Text.RegularExpressions.Regex.IsMatch(p.Slug, "^[a-z0-9-]{2,60}$")) return "Slug: só letras minúsculas, números e hífen.";
        if (p.Tipo is not ("assinatura" or "unico" or "licenca" or "teste")) return "Tipo inválido.";
        if (p.PrecoCentavos < 0 || p.Parcelas is < 1 or > 12) return "Preço ou parcelas inválidos.";
        if (p.DiasAcesso is <= 0) return "Dias de acesso deve ser positivo (ou vazio para vitalício).";
        foreach (var url in new[] { p.UrlKiwify, p.UrlHotmart })
            if (!string.IsNullOrWhiteSpace(url) && !(Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https"))
                return "Links de checkout precisam começar com https://";
        return null;
    }

    [HttpPost("produtos"), Authorize(Policy = "perm:" + P.ProdutosGerenciar)]
    public async Task<IActionResult> CriarProduto(ProdutoPedido p) => await SalvarProduto(null, p);

    [HttpPut("produtos/{id:int}"), Authorize(Policy = "perm:" + P.ProdutosGerenciar)]
    public async Task<IActionResult> EditarProduto(int id, ProdutoPedido p) => await SalvarProduto(id, p);

    async Task<IActionResult> SalvarProduto(int? id, ProdutoPedido p)
    {
        if (ValidarProduto(p) is { } erro) return Erro(erro);
        (string, object?)[] ps =
        [
            ("id", id), ("slug", p.Slug), ("nome", p.Nome!.Trim()), ("descricao", p.Descricao ?? ""), ("tipo", p.Tipo), ("preco", p.PrecoCentavos),
            ("de", p.PrecoDeCentavos), ("parc", p.Parcelas), ("dias", p.DiasAcesso), ("lib", (p.Liberacoes ?? []).Select(x => x.Trim().ToLowerInvariant()).Where(x => x != "").ToArray()),
            ("ben", (p.Beneficios ?? []).Select(x => x.Trim()).Where(x => x != "").ToArray()), ("kw", Vazio(p.UrlKiwify)), ("hm", Vazio(p.UrlHotmart)),
            ("mp", p.MercadoPago), ("ativo", p.Ativo), ("dest", p.Destaque), ("ordem", p.Ordem),
        ];
        try
        {
            if (id is null)
                id = await banco.Escalar<int>("""
                    INSERT INTO produtos (slug, nome, descricao, tipo, preco_centavos, preco_de_centavos, parcelas, dias_acesso, liberacoes, beneficios,
                                          url_kiwify, url_hotmart, mercado_pago, ativo, destaque, ordem)
                    VALUES (@slug, @nome, @descricao, @tipo, @preco, @de, @parc, @dias, @lib, @ben, @kw, @hm, @mp, @ativo, @dest, @ordem) RETURNING id
                    """, ps);
            else if (await banco.Executar("""
                    UPDATE produtos SET slug=@slug, nome=@nome, descricao=@descricao, tipo=@tipo, preco_centavos=@preco, preco_de_centavos=@de,
                        parcelas=@parc, dias_acesso=@dias, liberacoes=@lib, beneficios=@ben, url_kiwify=@kw, url_hotmart=@hm,
                        mercado_pago=@mp, ativo=@ativo, destaque=@dest, ordem=@ordem WHERE id=@id
                    """, ps) == 0) return NotFound();
        }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505") { return Erro("Já existe um produto com esse slug."); }
        await auditoria.Registrar("produto.salvo", p.Slug, p);
        return Ok(new { ok = true, id });
    }

    public sealed record ExternoPedido(string? Plataforma, string? IdExterno);

    [HttpPost("produtos/{id:int}/externos"), Authorize(Policy = "perm:" + P.ProdutosGerenciar)]
    public async Task<IActionResult> AdicionarExterno(int id, ExternoPedido p)
    {
        if (p.Plataforma is not ("kiwify" or "hotmart" or "mercadopago") || string.IsNullOrWhiteSpace(p.IdExterno)) return Erro("Plataforma ou id inválido.");
        try { await banco.Executar("INSERT INTO produtos_externos (plataforma, id_externo, produto_id) VALUES (@p, @e, @id)", ("p", p.Plataforma), ("e", p.IdExterno.Trim()), ("id", id)); }
        catch (Npgsql.PostgresException e) when (e.SqlState is "23505" or "23503") { return Erro("Esse id já está ligado a um produto (ou o produto não existe)."); }
        await auditoria.Registrar("produto.externo-adicionado", id.ToString(), p);
        return Ok(new { ok = true });
    }

    [HttpDelete("produtos/{id:int}/externos"), Authorize(Policy = "perm:" + P.ProdutosGerenciar)]
    public async Task<IActionResult> RemoverExterno(int id, string plataforma, string idExterno)
    {
        await banco.Executar("DELETE FROM produtos_externos WHERE produto_id=@id AND plataforma=@p AND id_externo=@e", ("id", id), ("p", plataforma), ("e", idExterno));
        await auditoria.Registrar("produto.externo-removido", id.ToString(), new { plataforma, idExterno });
        return Ok(new { ok = true });
    }

    // ================================================================ Cupons
    [HttpGet("cupons"), Authorize(Policy = "perm:" + P.CuponsGerenciar)]
    public async Task<IActionResult> Cupons() => Ok(await banco.Lista(
        "SELECT id, codigo, tipo, valor, minimo_centavos, valido_ate, max_usos, usos, produtos, ativo FROM cupons ORDER BY criado_em DESC",
        r => new { id = r.Inteiro("id"), codigo = r.Texto("codigo"), tipo = r.Texto("tipo"), valor = r.Inteiro("valor"),
            minimo = r.Inteiro("minimo_centavos"), validoAte = r.DataOuNula("valido_ate"), maxUsos = r.InteiroOuNulo("max_usos"),
            usos = r.Inteiro("usos"), produtos = r.Inteiros("produtos"), ativo = r.Logico("ativo") }));

    public sealed record CupomPedido(string? Codigo, string? Tipo, int Valor, int MinimoCentavos, DateTimeOffset? ValidoAte, int? MaxUsos, int[]? Produtos, bool Ativo);

    [HttpPost("cupons"), Authorize(Policy = "perm:" + P.CuponsGerenciar)]
    public async Task<IActionResult> SalvarCupom(CupomPedido p, int? id)
    {
        var codigo = (p.Codigo ?? "").Trim().ToUpperInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(codigo, "^[A-Z0-9_-]{3,30}$")) return Erro("Código: 3 a 30 letras, números, _ ou -.");
        if (p.Tipo is not ("percentual" or "fixo") || p.Valor <= 0 || (p.Tipo == "percentual" && p.Valor > 100)) return Erro("Tipo ou valor inválido.");
        (string, object?)[] ps = [("id", id), ("c", codigo), ("t", p.Tipo), ("v", p.Valor), ("m", Math.Max(0, p.MinimoCentavos)),
            ("va", p.ValidoAte?.ToUniversalTime()), ("mx", p.MaxUsos), ("pr", p.Produtos is { Length: > 0 } ? p.Produtos : null), ("a", p.Ativo)];
        try
        {
            if (id is null)
                await banco.Executar("INSERT INTO cupons (codigo, tipo, valor, minimo_centavos, valido_ate, max_usos, produtos, ativo) VALUES (@c,@t,@v,@m,@va,@mx,@pr,@a)", ps);
            else
                await banco.Executar("UPDATE cupons SET codigo=@c, tipo=@t, valor=@v, minimo_centavos=@m, valido_ate=@va, max_usos=@mx, produtos=@pr, ativo=@a WHERE id=@id", ps);
        }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505") { return Erro("Já existe um cupom com esse código."); }
        await auditoria.Registrar("cupom.salvo", codigo, p);
        return Ok(new { ok = true });
    }

    // ================================================================ Códigos de acesso (escolas, turmas, presentes)
    public sealed record GerarCodigosPedido(int ProdutoId, int Quantidade, int? Dias, string? Lote, DateTimeOffset? ValidoAte);

    [HttpPost("codigos/gerar"), Authorize(Policy = "perm:" + P.CodigosGerenciar)]
    public async Task<IActionResult> GerarCodigos(GerarCodigosPedido p)
    {
        if (p.Quantidade is < 1 or > 500) return Erro("Quantidade: de 1 a 500 por lote.");
        if (string.IsNullOrWhiteSpace(p.Lote) || p.Lote.Length > 60) return Erro("Dê um nome ao lote (ex.: Escola X — 3º ano A).");
        if (p.Dias is <= 0 or > 3650) return Erro("Dias inválidos.");
        if (await vendas.ProdutoPorId(p.ProdutoId) is null) return Erro("Produto não existe.");
        var codigos = new List<string>();
        while (codigos.Count < p.Quantidade)
        {
            var c = ServicoVendas.NovoCodigo();
            var n = await banco.Executar("""
                INSERT INTO codigos_acesso (codigo, produto_id, dias, lote, valido_ate, criado_por) VALUES (@c, @p, @d, @l, @v, @u)
                ON CONFLICT (codigo) DO NOTHING
                """, ("c", ServicoVendas.NormalizarCodigo(c)), ("p", p.ProdutoId), ("d", p.Dias), ("l", p.Lote.Trim()), ("v", p.ValidoAte?.ToUniversalTime()), ("u", MeuId));
            if (n == 1) codigos.Add(c);
        }
        await auditoria.Registrar("codigos.gerados", p.Lote, new { p.ProdutoId, p.Quantidade, p.Dias });
        return Ok(new { ok = true, codigos });
    }

    [HttpGet("codigos"), Authorize(Policy = "perm:" + P.CodigosGerenciar)]
    public async Task<IActionResult> Lotes() => Ok(await banco.Lista("""
        SELECT c.lote, pr.nome AS produto, count(*) AS total, count(c.usado_por) AS usados, min(c.criado_em) AS criado_em, max(c.valido_ate) AS valido_ate
        FROM codigos_acesso c JOIN produtos pr ON pr.id=c.produto_id GROUP BY c.lote, pr.nome ORDER BY min(c.criado_em) DESC
        """, r => new { lote = r.Texto("lote"), produto = r.Texto("produto"), total = r.Longo("total"), usados = r.Longo("usados"),
            criadoEm = r.Data("criado_em"), validoAte = r.DataOuNula("valido_ate") }));

    [HttpGet("codigos/lote.csv"), Authorize(Policy = "perm:" + P.CodigosGerenciar)]
    public async Task<IActionResult> LoteCsv(string lote)
    {
        var linhas = await banco.Lista("""
            SELECT c.codigo, coalesce(u.email,'') AS email, c.usado_em FROM codigos_acesso c LEFT JOIN usuarios u ON u.id=c.usado_por
            WHERE c.lote=@l ORDER BY c.id
            """, r =>
            {
                var c = r.Texto("codigo");
                return $"{c[..4]}-{c[4..8]}-{c[8..]};{Csv(r.Texto("email"))};{r.DataOuNula("usado_em")?.ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM/yyyy HH:mm")}";
            }, ("l", lote));
        var csv = "codigo;usado_por;usado_em\n" + string.Join('\n', linhas);
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", "codigos.csv");
    }

    // ================================================================ Regras de conteúdo
    [HttpGet("conteudo"), Authorize(Policy = "perm:" + P.ConteudoGerenciar)]
    public async Task<IActionResult> Regras() => Ok(await regras.Todas());

    public sealed record RegraPedido(string? Padrao, string? Nivel, string? Liberacao, int Ordem, string? Descricao);

    [HttpPost("conteudo"), Authorize(Policy = "perm:" + P.ConteudoGerenciar)]
    public async Task<IActionResult> SalvarRegra(RegraPedido p, int? id)
    {
        var padrao = (p.Padrao ?? "").Trim();
        if (padrao.Length is < 1 or > 200 || !(padrao.StartsWith('/') || padrao.StartsWith('*'))) return Erro("O caminho começa com / (ex.: /projeto-*.html).");
        if (p.Nivel is not ("livre" or "conta" or "pago" or "equipe" or "bloqueado")) return Erro("Nível inválido.");
        if (p.Nivel == "pago" && string.IsNullOrWhiteSpace(p.Liberacao)) return Erro("Conteúdo pago precisa da liberação (ex.: modulos).");
        (string, object?)[] ps = [("id", id), ("pa", padrao), ("n", p.Nivel), ("l", p.Nivel == "pago" ? p.Liberacao!.Trim().ToLowerInvariant() : null), ("o", p.Ordem), ("d", p.Descricao ?? "")];
        try
        {
            if (id is null) await banco.Executar("INSERT INTO regras_conteudo (padrao, nivel, liberacao, ordem, descricao) VALUES (@pa,@n,@l,@o,@d)", ps);
            else await banco.Executar("UPDATE regras_conteudo SET padrao=@pa, nivel=@n, liberacao=@l, ordem=@o, descricao=@d WHERE id=@id", ps);
        }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505") { return Erro("Já existe uma regra com esse caminho."); }
        regras.Recarregar();
        await auditoria.Registrar("conteudo.regra-salva", padrao, p);
        return Ok(new { ok = true });
    }

    [HttpDelete("conteudo/{id:int}"), Authorize(Policy = "perm:" + P.ConteudoGerenciar)]
    public async Task<IActionResult> ApagarRegra(int id)
    {
        var padrao = await banco.Escalar<string>("DELETE FROM regras_conteudo WHERE id=@id RETURNING padrao", ("id", id));
        regras.Recarregar();
        await auditoria.Registrar("conteudo.regra-apagada", padrao);
        return Ok(new { ok = true });
    }

    public sealed record TestarCaminhoPedido(string? Caminho);

    [HttpPost("conteudo/testar"), Authorize(Policy = "perm:" + P.ConteudoGerenciar)]
    public async Task<IActionResult> TestarCaminho(TestarCaminhoPedido p)
    {
        var r = await regras.Encontrar(p.Caminho ?? "/");
        return Ok(r is null ? new { regra = (Regra?)null, resultado = "Sem regra: a página NÃO é servida (404)." }
                            : new { regra = (Regra?)r, resultado = r.Nivel switch
                            {
                                "livre" => "Livre para todos.", "conta" => "Precisa de conta grátis.",
                                "pago" => $"Precisa de plano com a liberação \"{r.Liberacao}\".", "equipe" => "Só equipe.", _ => "Bloqueado (404).",
                            } });
    }

    // ================================================================ Webhooks
    [HttpGet("webhooks"), Authorize(Policy = "perm:" + P.WebhooksVer)]
    public async Task<IActionResult> Webhooks(string? plataforma, bool? comErro, int pagina = 1)
    {
        var lista = await banco.Lista($"""
            SELECT id, plataforma, tipo_evento, recebido_em, assinatura_valida, processado, resultado, tentativas, count(*) OVER () AS total
            FROM webhook_eventos
            WHERE (@p::text IS NULL OR plataforma=@p) AND (@e::boolean IS NULL OR (@e AND assinatura_valida AND NOT processado))
            ORDER BY recebido_em DESC LIMIT {PorPagina} OFFSET @o
            """, r => new { id = r.Longo("id"), plataforma = r.Texto("plataforma"), tipo = r.TextoOuNulo("tipo_evento"), recebidoEm = r.Data("recebido_em"),
                assinaturaValida = r.Logico("assinatura_valida"), processado = r.Logico("processado"), resultado = r.TextoOuNulo("resultado"),
                tentativas = r.Inteiro("tentativas"), total = r.Longo("total") },
            ("p", Vazio(plataforma)), ("e", comErro == true ? true : null), ("o", Offset(pagina)));
        return Ok(new { itens = lista, total = lista.FirstOrDefault()?.total ?? 0, porPagina = PorPagina });
    }

    [HttpGet("webhooks/{id:long}"), Authorize(Policy = "perm:" + P.WebhooksVer)]
    public async Task<IActionResult> Webhook(long id)
    {
        var w = await banco.Um("SELECT id, plataforma, corpo, consulta, resultado FROM webhook_eventos WHERE id=@id",
            r => new { id = r.Longo("id"), plataforma = r.Texto("plataforma"), corpo = r.Texto("corpo"), consulta = r.TextoOuNulo("consulta"), resultado = r.TextoOuNulo("resultado") }, ("id", id));
        return w is null ? NotFound() : Ok(w);
    }

    [HttpPost("webhooks/{id:long}/reprocessar"), Authorize(Policy = "perm:" + P.WebhooksReprocessar)]
    public async Task<IActionResult> Reprocessar(long id)
    {
        var r = await webhooks.Processar(id);
        await auditoria.Registrar("webhook.reprocessado", id.ToString(), new { resultado = r });
        return Ok(new { ok = true, resultado = r });
    }

    // ================================================================ Auditoria e integrações
    [HttpGet("auditoria"), Authorize(Policy = "perm:" + P.AuditoriaVer)]
    public async Task<IActionResult> Auditoria(string? busca, int pagina = 1)
    {
        var lista = await banco.Lista($"""
            SELECT quando, ator, acao, alvo, detalhes, ip, count(*) OVER () AS total FROM auditoria
            WHERE ator ILIKE @f OR acao ILIKE @f OR coalesce(alvo,'') ILIKE @f
            ORDER BY quando DESC LIMIT {PorPagina} OFFSET @o
            """, r => new { quando = r.Data("quando"), ator = r.Texto("ator"), acao = r.Texto("acao"), alvo = r.TextoOuNulo("alvo"),
                detalhes = r.TextoOuNulo("detalhes"), ip = r.TextoOuNulo("ip"), total = r.Longo("total") },
            ("f", $"%{(busca ?? "").Trim()}%"), ("o", Offset(pagina)));
        return Ok(new { itens = lista, total = lista.FirstOrDefault()?.total ?? 0, porPagina = PorPagina });
    }

    [HttpGet("integracoes"), Authorize(Policy = "perm:" + P.PainelVer)]
    public IActionResult Integracoes()
    {
        var url = (config["Site:UrlPublica"] ?? $"{Request.Scheme}://{Request.Host}").TrimEnd('/');
        return Ok(new
        {
            plataformas = plataformas.Select(p => new { nome = p.Nome, configurada = p.Configurada, urlWebhook = $"{url}/api/webhooks/{p.Nome}" }),
            email = string.IsNullOrWhiteSpace(config["Email:Smtp:Servidor"]) ? "modo teste (e-mails gravados em arquivo, NÃO enviados)" : "SMTP configurado",
            exige2faEquipe = config.GetValue("Seguranca:Exigir2faEquipe", true),
            urlPublica = url,
        });
    }
}
