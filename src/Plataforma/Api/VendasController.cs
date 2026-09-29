using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Plataforma.Dados;
using Plataforma.Identidade;
using Plataforma.Seguranca;
using Plataforma.Vendas;

namespace Plataforma.Api;

/// <summary>
/// Recebe os avisos das plataformas de pagamento.
/// Fluxo: guarda o aviso cru → confere assinatura → traduz → aplica. Tudo fica visível no painel
/// (Webhooks), e um aviso que falhou pode ser reprocessado depois de corrigir a causa.
/// </summary>
[ApiController, Route("api/webhooks"), SemCsrf, EnableRateLimiting("webhook")]
public sealed class WebhooksController(IEnumerable<IPlataformaVenda> plataformas, ProcessadorWebhooks processador, Banco banco,
    ILogger<WebhooksController> log) : ControllerBase
{
    const int TamanhoMaximo = 64 * 1024;

    [HttpPost("{plataforma}")]
    public async Task<IActionResult> Receber(string plataforma)
    {
        var p = plataformas.FirstOrDefault(x => x.Nome == plataforma);
        if (p is null) return NotFound();

        Request.EnableBuffering();
        using var leitor = new StreamReader(Request.Body);
        var corpo = await leitor.ReadToEndAsync();
        if (corpo.Length > TamanhoMaximo) return StatusCode(413);

        JsonNode? json;
        try { json = JsonNode.Parse(corpo); } catch { json = null; }
        if (json is null) return BadRequest();

        var valida = p.AssinaturaValida(Request, corpo);
        if (!valida)
        {
            log.LogWarning("Webhook {Plataforma} com assinatura inválida de {Ip}", plataforma, HttpContext.Connection.RemoteIpAddress);
            // Guarda um pedaço (até 2 mil letras) para você ver no painel se a configuração está errada.
            // No máximo 100 por hora: alguém mandando lixo não enche o disco.
            var recentes = await banco.Escalar<long>(
                "SELECT count(*) FROM webhook_eventos WHERE NOT assinatura_valida AND recebido_em > now() - interval '1 hour'");
            if (recentes < 100) await banco.Executar("""
                INSERT INTO webhook_eventos (plataforma, chave, tipo_evento, assinatura_valida, corpo, consulta, resultado)
                VALUES (@p, @c, @t, false, @b, @q, 'Assinatura inválida: não processado')
                ON CONFLICT (plataforma, chave) DO NOTHING
                """, ("p", plataforma), ("c", "invalido:" + Guid.NewGuid()), ("t", p.TipoEvento(Request, json)), ("b", corpo.Length > 2000 ? corpo[..2000] + "…" : corpo),
                  ("q", Request.QueryString.Value is { Length: > 500 } q ? q[..500] : Request.QueryString.Value));
            return Unauthorized();
        }

        var chave = p.Chave(Request, json);
        var id = await banco.Escalar<long?>("""
            INSERT INTO webhook_eventos (plataforma, chave, tipo_evento, assinatura_valida, corpo, consulta)
            VALUES (@p, @c, @t, true, @b, @q)
            ON CONFLICT (plataforma, chave) DO NOTHING RETURNING id
            """, ("p", plataforma), ("c", chave), ("t", p.TipoEvento(Request, json)), ("b", corpo), ("q", Request.QueryString.Value));
        if (id is null) return Ok(new { ok = true, duplicado = true }); // já recebido antes

        await processador.Processar(id.Value);
        // Responde 200 mesmo se a aplicação falhar: o aviso está guardado e pode ser reprocessado pelo painel.
        return Ok(new { ok = true });
    }
}

/// <summary>Aplica um webhook guardado (usado na chegada e no botão Reprocessar).</summary>
public sealed class ProcessadorWebhooks(IEnumerable<IPlataformaVenda> plataformas, ServicoVendas vendas, Banco banco, ILogger<ProcessadorWebhooks> log)
{
    public async Task<string> Processar(long id)
    {
        var ev = await banco.Um("SELECT plataforma, corpo, consulta, assinatura_valida FROM webhook_eventos WHERE id=@id",
            r => new { Plataforma = r.GetString(0), Corpo = r.GetString(1), Consulta = r.IsDBNull(2) ? "" : r.GetString(2), Valida = r.GetBoolean(3) }, ("id", id));
        if (ev is null) return "Evento não encontrado.";
        if (!ev.Valida) return "Assinatura inválida: não é processado.";

        string resultado; bool ok;
        try
        {
            var p = plataformas.First(x => x.Nome == ev.Plataforma);
            var venda = await p.Traduzir(ev.Consulta, JsonNode.Parse(ev.Corpo)!);
            resultado = venda is null ? "Sem dados de venda (evento informativo)." : await vendas.Processar(venda);
            ok = true;
        }
        catch (Exception e)
        {
            log.LogError(e, "Falha ao processar webhook {Id}", id);
            resultado = "ERRO: " + e.Message;
            ok = false;
        }
        await banco.Executar("UPDATE webhook_eventos SET processado=@ok, resultado=@r, tentativas=tentativas+1 WHERE id=@id",
            ("ok", ok), ("r", resultado), ("id", id));
        return resultado;
    }
}

/// <summary>Planos à venda (público), cupom e checkout do Mercado Pago.</summary>
[ApiController, Route("api/planos"), ValidarCsrf]
public sealed class PlanosController(ServicoVendas vendas, Banco banco, MercadoPago mp, UserManager<Usuario> usuarios, IConfiguration config) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var lista = await banco.Lista($"SELECT {Produto.Colunas} FROM produtos WHERE ativo ORDER BY ordem, id", Produto.Ler);
        return Ok(new
        {
            mercadoPagoAtivo = mp.Configurada,
            planos = lista.Select(p => new
            {
                p.Slug, p.Nome, p.Descricao, p.Tipo, preco = p.PrecoCentavos, precoDe = p.PrecoDeCentavos, p.Parcelas,
                dias = p.DiasAcesso, p.Liberacoes, p.Beneficios, p.Destaque,
                kiwify = p.UrlKiwify, hotmart = p.UrlHotmart, mercadoPago = p.MercadoPago && mp.Configurada && p.Tipo != "assinatura",
            }),
        });
    }

    public sealed record CupomPedido(string? Plano, string? Cupom);

    [HttpPost("cupom"), EnableRateLimiting("sensivel")]
    public async Task<IActionResult> ValidarCupom(CupomPedido p)
    {
        var prod = p.Plano is null ? null : await vendas.ProdutoPorSlug(p.Plano);
        if (prod is null || !prod.Ativo) return BadRequest(new { erro = "Plano não encontrado." });
        var r = await vendas.AplicarCupom(p.Cupom, prod);
        return Ok(new { valido = r.Valido, mensagem = r.Mensagem, desconto = r.DescontoCentavos, total = r.TotalCentavos });
    }

    [Authorize, HttpPost("checkout"), EnableRateLimiting("sensivel")]
    public async Task<IActionResult> Checkout(CupomPedido p)
    {
        if (!mp.Configurada) return BadRequest(new { erro = "Pagamento direto indisponível no momento." });
        var prod = p.Plano is null ? null : await vendas.ProdutoPorSlug(p.Plano);
        if (prod is null || !prod.Ativo || !prod.MercadoPago || prod.Tipo == "assinatura")
            return BadRequest(new { erro = "Este plano não pode ser comprado por aqui." });
        var cupom = await vendas.AplicarCupom(p.Cupom, prod);
        if (!cupom.Valido) return BadRequest(new { erro = cupom.Mensagem });
        if (cupom.TotalCentavos < 100) return BadRequest(new { erro = "Valor mínimo para pagamento: R$ 1,00." });

        var eu = (await usuarios.GetUserAsync(User))!;
        if (await banco.Escalar<long>("SELECT count(*) FROM pedidos WHERE usuario_id=@u AND plataforma='mercadopago' AND status='pendente'",
                ("u", eu.Id)) >= 3)
            return BadRequest(new { erro = "Você já tem 3 pagamentos em aberto. Conclua um deles ou espere 24 horas." });

        // Reserva um uso do cupom AGORA, de forma atômica: dez checkouts abertos ao mesmo tempo
        // não passam do limite. Se o pagamento for recusado ou expirar, o uso volta.
        if (cupom.CupomId is not null &&
            await banco.Escalar<int?>("UPDATE cupons SET usos = usos + 1 WHERE id=@id AND (max_usos IS NULL OR usos < max_usos) RETURNING id",
                ("id", cupom.CupomId)) is null)
            return BadRequest(new { erro = "Este cupom já atingiu o limite de usos." });

        var u = (await usuarios.GetUserAsync(User))!;
        var pedidoId = Guid.CreateVersion7();
        await banco.Executar("""
            INSERT INTO pedidos (id, usuario_id, email_comprador, email_normalizado, nome_comprador, produto_id, plataforma, status, valor_centavos, cupom_id)
            VALUES (@id, @u, @e, @en, @n, @p, 'mercadopago', 'pendente', @v, @c)
            """, ("id", pedidoId), ("u", u.Id), ("e", u.Email), ("en", u.EmailNormalizado), ("n", u.Nome), ("p", prod.Id),
            ("v", cupom.TotalCentavos), ("c", cupom.CupomId));
        var url = (config["Site:UrlPublica"] ?? $"{Request.Scheme}://{Request.Host}").TrimEnd('/');
        try
        {
            var link = await mp.CriarCheckout(pedidoId, prod.Nome, cupom.TotalCentavos, u.Email, url);
            return Ok(new { url = link });
        }
        catch (Exception ex)
        {
            // O Mercado Pago não respondeu: desfaz o pedido e devolve o uso do cupom
            HttpContext.RequestServices.GetRequiredService<ILogger<PlanosController>>().LogError(ex, "Falha ao criar checkout do Mercado Pago");
            await banco.Executar("UPDATE pedidos SET status='recusado', atualizado_em=now() WHERE id=@id", ("id", pedidoId));
            if (cupom.CupomId is not null)
                await banco.Executar("UPDATE cupons SET usos = greatest(usos - 1, 0) WHERE id=@id", ("id", cupom.CupomId));
            return StatusCode(502, new { erro = "O pagamento está indisponível agora. Tente de novo em alguns minutos." });
        }
    }
}
