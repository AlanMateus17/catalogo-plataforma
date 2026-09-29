using System.Security.Cryptography;
using Npgsql;
using Plataforma.Acesso;
using Plataforma.Auditoria;
using Plataforma.Dados;
using Plataforma.Email;
using Plataforma.Identidade;

namespace Plataforma.Vendas;

/// <summary>
/// Um aviso de venda já traduzido para a nossa língua, venha de onde vier
/// (Kiwify, Hotmart, Mercado Pago). Tipo: aprovado, reembolsado, chargeback,
/// pendente, recusado, expirado, atrasado, cancelado_assinatura, ignorar.
/// </summary>
public sealed record EventoVenda(
    string Plataforma, string Tipo, string IdExterno, string Email, string? Nome,
    string? ProdutoExterno, string? OfertaExterna, int ValorCentavos, string? AssinaturaExterna,
    Guid? PedidoInterno = null);

public sealed record Produto(int Id, string Slug, string Nome, string Descricao, string Tipo, int PrecoCentavos, int? PrecoDeCentavos,
    int Parcelas, int? DiasAcesso, string[] Liberacoes, string[] Beneficios, string? UrlKiwify, string? UrlHotmart, bool MercadoPago,
    bool Ativo, bool Destaque, int Ordem)
{
    public static Produto Ler(System.Data.Common.DbDataReader r) => new(r.Inteiro("id"), r.Texto("slug"), r.Texto("nome"), r.Texto("descricao"),
        r.Texto("tipo"), r.Inteiro("preco_centavos"), r.InteiroOuNulo("preco_de_centavos"), r.Inteiro("parcelas"), r.InteiroOuNulo("dias_acesso"),
        r.Textos("liberacoes"), r.Textos("beneficios"), r.TextoOuNulo("url_kiwify"), r.TextoOuNulo("url_hotmart"), r.Logico("mercado_pago"),
        r.Logico("ativo"), r.Logico("destaque"), r.Inteiro("ordem"));
    public const string Colunas = "id, slug, nome, descricao, tipo, preco_centavos, preco_de_centavos, parcelas, dias_acesso, liberacoes, beneficios, url_kiwify, url_hotmart, mercado_pago, ativo, destaque, ordem";
}

public sealed class ServicoVendas(Banco banco, IEnviadorEmail email, ModelosEmail modelos, RegistroAuditoria auditoria, ILogger<ServicoVendas> log)
{
    /// <summary>Dias extras nas assinaturas: a renovação às vezes chega um ou dois dias depois do vencimento.</summary>
    public const int FolgaAssinaturaDias = 3;

    public Task<Produto?> ProdutoPorId(int id) => banco.Um($"SELECT {Produto.Colunas} FROM produtos WHERE id=@id", Produto.Ler, ("id", id));
    public Task<Produto?> ProdutoPorSlug(string slug) => banco.Um($"SELECT {Produto.Colunas} FROM produtos WHERE slug=@s", Produto.Ler, ("s", slug));

    // =================================================================== Webhooks
    /// <summary>
    /// Aplica um evento de venda. Pode ser chamado de novo com o mesmo evento sem efeito duplicado
    /// (idempotente): um pedido já aprovado não gera um segundo acesso.
    /// </summary>
    public async Task<string> Processar(EventoVenda e)
    {
        if (e.Tipo == "ignorar") return "Evento ignorado (não muda acesso).";
        var emailNorm = Emails.Normalizar(e.Email);
        var avisos = new List<Func<Task>>();

        var resultado = await banco.Transacao(async (con, tx) =>
        {
            // 1. Qual pedido?
            Guid? pedidoId = null; string? statusAtual = null; int? produtoId = null;
            var sqlPedido = e.PedidoInterno is not null
                ? "SELECT id, status, produto_id FROM pedidos WHERE id=@id FOR UPDATE"
                : "SELECT id, status, produto_id FROM pedidos WHERE plataforma=@pl AND id_externo=@ext FOR UPDATE";
            await using (var c = Banco.Comando(con, tx, sqlPedido, ("id", e.PedidoInterno), ("pl", e.Plataforma), ("ext", e.IdExterno)))
            await using (var r = await c.ExecuteReaderAsync())
                if (await r.ReadAsync()) { pedidoId = r.GetGuid(0); statusAtual = r.GetString(1); produtoId = r.GetInt32(2); }

            // 2. Qual produto? (pedido existente, ou o mapeamento da plataforma)
            produtoId ??= await ProdutoMapeado(con, tx, e);
            if (produtoId is null)
                throw new InvalidOperationException($"Produto da plataforma não mapeado ({e.Plataforma}: {e.ProdutoExterno}/{e.OfertaExterna}). " +
                    "Cadastre o id no painel (Produtos → ids nas plataformas) e clique em Reprocessar.");

            string? tipoProduto; string nomeProduto; int? diasProduto;
            await using (var c = Banco.Comando(con, tx, "SELECT tipo, nome, dias_acesso FROM produtos WHERE id=@p", ("p", produtoId)))
            await using (var r = await c.ExecuteReaderAsync())
            {
                await r.ReadAsync();
                tipoProduto = r.GetString(0); nomeProduto = r.GetString(1); diasProduto = r.IsDBNull(2) ? null : r.GetInt32(2);
            }

            var novoStatus = e.Tipo switch
            {
                "aprovado" => "aprovado", "reembolsado" => "reembolsado", "chargeback" => "chargeback",
                "pendente" => "pendente", "recusado" => "recusado", "expirado" => "expirado", _ => null,
            };

            // Eventos que não mudam o pedido: só registram
            if (novoStatus is null)
                return e.Tipo switch
                {
                    "cancelado_assinatura" => "Assinatura cancelada: o acesso continua até o fim do período já pago.",
                    "atrasado" => "Pagamento da assinatura atrasado: o acesso termina sozinho no fim do período, se não for pago.",
                    _ => $"Evento {e.Tipo} registrado.",
                };

            if (statusAtual == novoStatus) return $"Nada a fazer: pedido já está '{novoStatus}'.";
            // Um pedido reembolsado ou com chargeback não volta a liberar acesso por um aviso atrasado de "aprovado".
            if (novoStatus == "aprovado" && statusAtual is "reembolsado" or "chargeback")
                return $"Ignorado: pedido já está '{statusAtual}'.";
            if (novoStatus == "pendente" && statusAtual == "aprovado")
                return "Ignorado: pedido já aprovado.";

            // 3. Grava o pedido
            if (pedidoId is null)
            {
                pedidoId = Guid.CreateVersion7();
                await using var c = Banco.Comando(con, tx, """
                    INSERT INTO pedidos (id, email_comprador, email_normalizado, nome_comprador, produto_id, plataforma, id_externo,
                                         assinatura_externa, status, valor_centavos)
                    VALUES (@id, @e, @en, @n, @p, @pl, @ext, @ass, @st, @v)
                    """, ("id", pedidoId), ("e", e.Email.Trim()), ("en", emailNorm), ("n", e.Nome), ("p", produtoId), ("pl", e.Plataforma),
                    ("ext", e.IdExterno), ("ass", e.AssinaturaExterna), ("st", novoStatus), ("v", e.ValorCentavos));
                await c.ExecuteNonQueryAsync();
            }
            else
            {
                await using var c = Banco.Comando(con, tx, """
                    UPDATE pedidos SET status=@st, atualizado_em=now(), id_externo=coalesce(id_externo, @ext),
                        valor_centavos=CASE WHEN @v > 0 THEN @v ELSE valor_centavos END
                    WHERE id=@id
                    """, ("st", novoStatus), ("ext", e.IdExterno), ("v", e.ValorCentavos), ("id", pedidoId));
                await c.ExecuteNonQueryAsync();
            }

            // 4. Libera ou retira acesso
            switch (novoStatus)
            {
                case "aprovado":
                    var conta = await ServicoAcesso.ContaConfirmada(con, tx, emailNorm);
                    int? dias = diasProduto is null ? null : diasProduto + (tipoProduto == "assinatura" ? FolgaAssinaturaDias : 0);
                    var (_, fim) = await ServicoAcesso.Conceder(con, tx, conta, emailNorm, produtoId.Value, pedidoId, e.Plataforma, dias, null, null);
                    await using (var c = Banco.Comando(con, tx, "UPDATE pedidos SET usuario_id=@u WHERE id=@id", ("u", conta), ("id", pedidoId)))
                        await c.ExecuteNonQueryAsync();
                    // O uso do cupom foi reservado no checkout (VendasController.Checkout) e devolvido se o pedido
                    // foi recusado ou expirou. Pago depois disso: conta o uso de novo (o pagamento já aconteceu).
                    if (statusAtual is "recusado" or "expirado")
                        await using (var c = Banco.Comando(con, tx,
                            "UPDATE cupons SET usos = usos + 1 WHERE id = (SELECT cupom_id FROM pedidos WHERE id=@id)", ("id", pedidoId)))
                            await c.ExecuteNonQueryAsync();
                    var nome = e.Nome ?? "aluno";
                    avisos.Add(() =>
                    {
                        var (a, h) = conta is not null ? modelos.AcessoLiberado(nome, nomeProduto, fim) : modelos.CompraSemConta(nomeProduto, e.Email.Trim());
                        return email.Enviar(e.Email.Trim(), a, h);
                    });
                    return conta is not null ? $"Aprovado: acesso liberado até {fim?.ToString("dd/MM/yyyy") ?? "sempre"}."
                                             : "Aprovado: acesso reservado para o e-mail (a pessoa ainda não tem conta confirmada).";

                case "reembolsado":
                case "chargeback":
                    var n = await ServicoAcesso.RevogarDoPedido(con, tx, pedidoId.Value, novoStatus);
                    // Assinatura: o reembolso/chargeback também retira o acesso das renovações da mesma assinatura
                    await using (var c = Banco.Comando(con, tx, """
                        UPDATE acessos SET status='revogado', motivo=@m
                        WHERE status='ativo' AND pedido_id IN (
                            SELECT id FROM pedidos WHERE plataforma=@pl AND assinatura_externa IS NOT NULL
                              AND assinatura_externa = coalesce(@ass, (SELECT assinatura_externa FROM pedidos WHERE id=@id)))
                        """, ("m", novoStatus), ("pl", e.Plataforma), ("ass", e.AssinaturaExterna), ("id", pedidoId)))
                        n += await c.ExecuteNonQueryAsync();
                    return $"Pedido {novoStatus}: {n} acesso(s) revogado(s).";

                case "recusado":
                case "expirado":
                    // Devolve o uso do cupom reservado no checkout (só se o pedido estava pendente)
                    if (statusAtual is null or "pendente")
                        await using (var c = Banco.Comando(con, tx,
                            "UPDATE cupons SET usos = greatest(usos - 1, 0) WHERE id = (SELECT cupom_id FROM pedidos WHERE id=@id)", ("id", pedidoId)))
                            await c.ExecuteNonQueryAsync();
                    return $"Pedido marcado como '{novoStatus}'.";

                default:
                    return $"Pedido marcado como '{novoStatus}'.";
            }
        });

        foreach (var aviso in avisos)
        {
            try { await aviso(); }
            catch (Exception ex) { log.LogError(ex, "Falha ao enviar e-mail da venda {Id}", e.IdExterno); }
        }
        await auditoria.Registrar($"venda.{e.Tipo}", e.Email, new { e.Plataforma, e.IdExterno, resultado }, ator: "webhook:" + e.Plataforma);
        return resultado;
    }

    static async Task<int?> ProdutoMapeado(NpgsqlConnection con, NpgsqlTransaction tx, EventoVenda e)
    {
        foreach (var chave in new[] { e.OfertaExterna is null ? null : $"{e.ProdutoExterno}:{e.OfertaExterna}", e.ProdutoExterno })
        {
            if (chave is null) continue;
            await using var c = Banco.Comando(con, tx, "SELECT produto_id FROM produtos_externos WHERE plataforma=@pl AND id_externo=@id",
                ("pl", e.Plataforma), ("id", chave));
            if (await c.ExecuteScalarAsync() is int id) return id;
        }
        return null;
    }

    // =================================================================== Teste grátis
    public async Task<(bool ok, string mensagem)> IniciarTeste(Usuario u)
    {
        var teste = await ProdutoPorSlug("teste-gratis");
        if (teste is null) return (false, "O teste grátis não está disponível agora.");
        var r = await banco.Transacao(async (con, tx) =>
        {
            // FOR UPDATE: dois cliques ao mesmo tempo não geram dois testes
            await using (var c = Banco.Comando(con, tx, "SELECT teste_usado_em FROM usuarios WHERE id=@u FOR UPDATE", ("u", u.Id)))
                if (await c.ExecuteScalarAsync() is DateTime) return (false, "Você já usou o teste grátis nesta conta.");
            await using (var c = Banco.Comando(con, tx, """
                SELECT EXISTS (SELECT 1 FROM acessos WHERE usuario_id=@u AND status='ativo' AND (fim IS NULL OR fim > now()))
                """, ("u", u.Id)))
                if (await c.ExecuteScalarAsync() is true) return (false, "Você já tem um plano ativo.");
            // Um teste por caixa de e-mail (ignora +apelido e, no Gmail, os pontos)
            await using (var c = Banco.Comando(con, tx,
                "INSERT INTO testes_usados (chave) VALUES (@k) ON CONFLICT DO NOTHING RETURNING chave", ("k", ChaveTeste(u.Email))))
                if (await c.ExecuteScalarAsync() is null) return (false, "Este e-mail já usou o teste grátis.");

            var pedido = Guid.CreateVersion7();
            await using (var c = Banco.Comando(con, tx, """
                INSERT INTO pedidos (id, usuario_id, email_comprador, email_normalizado, nome_comprador, produto_id, plataforma, id_externo, status, valor_centavos)
                VALUES (@id, @u, @e, @en, @n, @p, 'teste', @ext, 'aprovado', 0)
                """, ("id", pedido), ("u", u.Id), ("e", u.Email), ("en", u.EmailNormalizado), ("n", u.Nome), ("p", teste.Id), ("ext", "teste-" + u.Id)))
                await c.ExecuteNonQueryAsync();
            var (_, fim) = await ServicoAcesso.Conceder(con, tx, u.Id, u.EmailNormalizado, teste.Id, pedido, "teste", null, "teste grátis", u.Id);
            await using (var c = Banco.Comando(con, tx, "UPDATE usuarios SET teste_usado_em=now() WHERE id=@u", ("u", u.Id)))
                await c.ExecuteNonQueryAsync();
            return (true, $"Teste liberado até {fim!.Value.ToOffset(TimeSpan.FromHours(-3)):dd/MM/yyyy 'às' HH:mm}. Bons estudos!");
        });
        if (r.Item1) await auditoria.Registrar("teste.iniciado", u.Email);
        return r;
    }

    /// <summary>E-mail canônico (sem +apelido; Gmail sem pontos) → SHA-256. Usado só para o teste grátis.</summary>
    public static string ChaveTeste(string email)
    {
        var e = email.Trim().ToLowerInvariant();
        var arroba = e.LastIndexOf('@');
        if (arroba > 0)
        {
            var (local, dominio) = (e[..arroba], e[(arroba + 1)..]);
            var mais = local.IndexOf('+');
            if (mais > 0) local = local[..mais];
            if (dominio is "gmail.com" or "googlemail.com") { local = local.Replace(".", ""); dominio = "gmail.com"; }
            e = local + "@" + dominio;
        }
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(e)));
    }

    // =================================================================== Códigos de acesso
    public static string NormalizarCodigo(string c) => new string(c.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    public async Task<(bool ok, string mensagem)> ResgatarCodigo(Usuario u, string codigoDigitado)
    {
        var codigo = NormalizarCodigo(codigoDigitado);
        if (codigo.Length is < 8 or > 20) return (false, "Código inválido.");
        var r = await banco.Transacao(async (con, tx) =>
        {
            long id; int produto; int? dias; DateTime? validade; Guid? usadoPor; Guid? criadoPor;
            await using (var c = Banco.Comando(con, tx,
                "SELECT id, produto_id, dias, valido_ate, usado_por, criado_por FROM codigos_acesso WHERE codigo=@c FOR UPDATE", ("c", codigo)))
            await using (var rd = await c.ExecuteReaderAsync())
            {
                if (!await rd.ReadAsync()) return (false, "Código inválido.");
                id = rd.GetInt64(0); produto = rd.GetInt32(1); dias = rd.IsDBNull(2) ? null : rd.GetInt32(2);
                validade = rd.IsDBNull(3) ? null : rd.GetDateTime(3); usadoPor = rd.IsDBNull(4) ? null : rd.GetGuid(4);
                criadoPor = rd.IsDBNull(5) ? null : rd.GetGuid(5);
            }
            if (criadoPor == u.Id) return (false, "Quem gerou o código não pode usá-lo na própria conta.");
            if (usadoPor is not null) return (false, usadoPor == u.Id ? "Você já usou este código." : "Este código já foi usado.");
            if (validade is not null && validade < DateTime.UtcNow) return (false, "Este código venceu.");

            var pedido = Guid.CreateVersion7();
            await using (var c = Banco.Comando(con, tx, """
                INSERT INTO pedidos (id, usuario_id, email_comprador, email_normalizado, nome_comprador, produto_id, plataforma, id_externo, status, valor_centavos)
                VALUES (@id, @u, @e, @en, @n, @p, 'codigo', @ext, 'aprovado', 0)
                """, ("id", pedido), ("u", u.Id), ("e", u.Email), ("en", u.EmailNormalizado), ("n", u.Nome), ("p", produto), ("ext", codigo)))
                await c.ExecuteNonQueryAsync();
            var (_, fim) = await ServicoAcesso.Conceder(con, tx, u.Id, u.EmailNormalizado, produto, pedido, "codigo", dias, "código " + codigo, u.Id);
            await using (var c = Banco.Comando(con, tx, "UPDATE codigos_acesso SET usado_por=@u, usado_em=now() WHERE id=@id", ("u", u.Id), ("id", id)))
                await c.ExecuteNonQueryAsync();
            return (true, fim is null ? "Código aceito: acesso liberado." : $"Código aceito: acesso liberado até {fim.Value.ToOffset(TimeSpan.FromHours(-3)):dd/MM/yyyy}.");
        });
        await auditoria.Registrar(r.Item1 ? "codigo.resgatado" : "codigo.recusado", u.Email, new { codigo = codigo[..4] + "…", r.Item2 });
        return r;
    }

    /// <summary>Gera códigos fáceis de digitar (sem 0/O, 1/I/L), no formato XXXX-XXXX-XXXX.</summary>
    public static string NovoCodigo()
    {
        const string letras = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        Span<char> s = stackalloc char[12];
        for (var i = 0; i < s.Length; i++) s[i] = letras[RandomNumberGenerator.GetInt32(letras.Length)];
        return $"{s[..4]}-{s[4..8]}-{s[8..]}";
    }

    // =================================================================== Cupons
    public sealed record ResultadoCupom(bool Valido, string Mensagem, int? CupomId, int DescontoCentavos, int TotalCentavos);

    public async Task<ResultadoCupom> AplicarCupom(string? codigoCupom, Produto p)
    {
        if (string.IsNullOrWhiteSpace(codigoCupom)) return new(true, "", null, 0, p.PrecoCentavos);
        var c = await banco.Um("""
            SELECT id, tipo, valor, minimo_centavos, valido_ate, max_usos, usos, produtos, ativo FROM cupons WHERE codigo=upper(@c)
            """, r => new
            {
                Id = r.Inteiro("id"), Tipo = r.Texto("tipo"), Valor = r.Inteiro("valor"), Minimo = r.Inteiro("minimo_centavos"),
                Validade = r.DataOuNula("valido_ate"), Max = r.InteiroOuNulo("max_usos"), Usos = r.Inteiro("usos"),
                Produtos = r.Inteiros("produtos"), Ativo = r.Logico("ativo"),
            }, ("c", codigoCupom.Trim()));
        if (c is null || !c.Ativo) return new(false, "Cupom inválido.", null, 0, p.PrecoCentavos);
        if (c.Validade is not null && c.Validade < DateTimeOffset.UtcNow) return new(false, "Cupom vencido.", null, 0, p.PrecoCentavos);
        if (c.Max is not null && c.Usos >= c.Max) return new(false, "Este cupom já atingiu o limite de usos.", null, 0, p.PrecoCentavos);
        if (c.Produtos is { Length: > 0 } && !c.Produtos.Contains(p.Id)) return new(false, "Este cupom não vale para este plano.", null, 0, p.PrecoCentavos);
        if (p.PrecoCentavos < c.Minimo) return new(false, $"Compra mínima de {Dinheiro(c.Minimo)} para este cupom.", null, 0, p.PrecoCentavos);
        var desconto = c.Tipo == "percentual" ? (int)Math.Round(p.PrecoCentavos * c.Valor / 100m, MidpointRounding.AwayFromZero) : c.Valor;
        desconto = Math.Min(desconto, p.PrecoCentavos);
        return new(true, $"Cupom aplicado: menos {Dinheiro(desconto)}.", c.Id, desconto, p.PrecoCentavos - desconto);
    }

    public static string Dinheiro(int centavos) => (centavos / 100m).ToString("C", new System.Globalization.CultureInfo("pt-BR"));

    // =================================================================== Venda manual (Pix direto, dinheiro, escola)
    public async Task<string> VendaManual(Guid adminId, string emailComprador, string? nome, int produtoId, int valorCentavos, string observacao, int? dias)
    {
        var evento = new EventoVenda("manual", "aprovado", "manual-" + Guid.CreateVersion7().ToString("N"), emailComprador, nome,
            null, null, valorCentavos, null);
        // Venda manual: cria o pedido direto com o produto escolhido
        var pedidoId = Guid.CreateVersion7();
        var emailNorm = Emails.Normalizar(emailComprador);
        var p = await ProdutoPorId(produtoId) ?? throw new InvalidOperationException("Produto não existe.");
        var (conta, fim) = await banco.Transacao(async (con, tx) =>
        {
            var conta = await ServicoAcesso.ContaConfirmada(con, tx, emailNorm);
            await using (var c = Banco.Comando(con, tx, """
                INSERT INTO pedidos (id, usuario_id, email_comprador, email_normalizado, nome_comprador, produto_id, plataforma, id_externo, status, valor_centavos, observacao)
                VALUES (@id, @u, @e, @en, @n, @p, 'manual', @ext, 'aprovado', @v, @obs)
                """, ("id", pedidoId), ("u", conta), ("e", emailComprador.Trim()), ("en", emailNorm), ("n", nome), ("p", produtoId),
                ("ext", evento.IdExterno), ("v", valorCentavos), ("obs", observacao)))
                await c.ExecuteNonQueryAsync();
            var (_, fim) = await ServicoAcesso.Conceder(con, tx, conta, emailNorm, produtoId, pedidoId, "manual", dias, observacao, adminId);
            return (conta, fim);
        });
        var (a, h) = conta is not null ? modelos.AcessoLiberado(nome ?? "aluno", p.Nome, fim) : modelos.CompraSemConta(p.Nome, emailComprador.Trim());
        try { await email.Enviar(emailComprador.Trim(), a, h); } catch (Exception ex) { log.LogError(ex, "Falha no e-mail da venda manual"); }
        await auditoria.Registrar("venda.manual", emailComprador, new { produto = p.Slug, valorCentavos, observacao, pedidoId });
        return conta is not null ? "Venda registrada e acesso liberado." : "Venda registrada. O acesso será liberado quando a pessoa criar a conta com este e-mail.";
    }

    public async Task<string> Reembolsar(Guid pedidoId, string motivo)
    {
        var n = await banco.Transacao(async (con, tx) =>
        {
            await using (var c = Banco.Comando(con, tx,
                "UPDATE pedidos SET status='reembolsado', atualizado_em=now(), observacao=concat_ws(' | ', observacao, @m) WHERE id=@id AND status='aprovado'",
                ("id", pedidoId), ("m", "Reembolso: " + motivo)))
                if (await c.ExecuteNonQueryAsync() == 0) return -1;
            return await ServicoAcesso.RevogarDoPedido(con, tx, pedidoId, "reembolso: " + motivo);
        });
        if (n < 0) return "Só pedidos aprovados podem ser reembolsados.";
        await auditoria.Registrar("pedido.reembolsado", pedidoId.ToString(), new { motivo });
        return $"Pedido marcado como reembolsado; {n} acesso(s) revogado(s). Lembre-se de devolver o dinheiro na plataforma de pagamento.";
    }
}
