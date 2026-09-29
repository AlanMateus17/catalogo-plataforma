using Npgsql;
using Plataforma.Dados;

namespace Plataforma.Acesso;

public sealed record AcessoVisao(long Id, string Produto, string ProdutoSlug, string[] Liberacoes, DateTimeOffset Inicio,
    DateTimeOffset? Fim, string Status, string Origem, string? Motivo, bool Vigente);

/// <summary>
/// Tudo sobre "quem pode ver o quê": conceder, revogar e consultar acessos.
/// Um acesso vale quando: status = ativo, já começou, não terminou e está ligado a uma conta.
/// </summary>
public sealed class ServicoAcesso(Banco banco)
{
    const string Vigente = "a.status = 'ativo' AND a.inicio <= now() AND (a.fim IS NULL OR a.fim > now())";

    /// <summary>As chaves liberadas para o usuário agora (ex.: modulos, projetos).</summary>
    public async Task<HashSet<string>> Liberacoes(Guid usuarioId) =>
        (await banco.Lista($"""
            SELECT DISTINCT unnest(p.liberacoes) FROM acessos a JOIN produtos p ON p.id = a.produto_id
            WHERE a.usuario_id = @u AND {Vigente}
            """, r => r.GetString(0), ("u", usuarioId))).ToHashSet();

    public Task<List<AcessoVisao>> DoUsuario(Guid usuarioId) => banco.Lista($"""
        SELECT a.id, p.nome, p.slug, p.liberacoes, a.inicio, a.fim, a.status, a.origem, a.motivo, ({Vigente}) AS vigente
        FROM acessos a JOIN produtos p ON p.id = a.produto_id
        WHERE a.usuario_id = @u ORDER BY a.inicio DESC
        """, r => new AcessoVisao(r.Longo("id"), r.Texto("nome"), r.Texto("slug"), r.Textos("liberacoes"), r.Data("inicio"),
            r.DataOuNula("fim"), r.Texto("status"), r.Texto("origem"), r.TextoOuNulo("motivo"), r.Logico("vigente")), ("u", usuarioId));

    /// <summary>
    /// Concede acesso a um produto. Se a pessoa já tem acesso ao MESMO produto,
    /// os dias novos são somados ao fim do atual (renovação não "come" dias).
    /// usuarioId nulo = compra de e-mail ainda sem conta (fica reservada).
    /// </summary>
    public static async Task<(long id, DateTimeOffset? fim)> Conceder(NpgsqlConnection con, NpgsqlTransaction tx,
        Guid? usuarioId, string emailNormalizado, int produtoId, Guid? pedidoId, string origem,
        int? dias, string? motivo, Guid? criadoPor)
    {
        await using (var c = Banco.Comando(con, tx, "SELECT dias_acesso FROM produtos WHERE id=@p", ("p", produtoId)))
        {
            var r = await c.ExecuteScalarAsync();
            dias ??= r is DBNull or null ? null : (int)r;
        }

        DateTimeOffset? fim = null;
        if (dias is not null)
        {
            // Ponto de partida: agora, ou o fim do acesso vigente mais longo ao mesmo produto
            await using var c = Banco.Comando(con, tx, $"""
                SELECT max(a.fim) FROM acessos a
                WHERE a.produto_id=@p AND a.email_normalizado=@e AND {Vigente} AND a.fim IS NOT NULL
                """, ("p", produtoId), ("e", emailNormalizado));
            var r = await c.ExecuteScalarAsync();
            var base_ = r is DateTime d ? new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)) : DateTimeOffset.UtcNow;
            if (base_ < DateTimeOffset.UtcNow) base_ = DateTimeOffset.UtcNow;
            fim = base_.AddDays(dias.Value);
        }

        await using var ins = Banco.Comando(con, tx, """
            INSERT INTO acessos (usuario_id, email_normalizado, produto_id, pedido_id, inicio, fim, origem, motivo, criado_por)
            VALUES (@u, @e, @p, @ped, now(), @fim, @o, @m, @c) RETURNING id
            """, ("u", usuarioId), ("e", emailNormalizado), ("p", produtoId), ("ped", pedidoId), ("fim", fim?.ToUniversalTime()),
            ("o", origem), ("m", motivo), ("c", criadoPor));
        var id = (long)(await ins.ExecuteScalarAsync())!;
        return (id, fim);
    }

    public static async Task<int> RevogarDoPedido(NpgsqlConnection con, NpgsqlTransaction tx, Guid pedidoId, string motivo)
    {
        await using var c = Banco.Comando(con, tx,
            "UPDATE acessos SET status='revogado', motivo=@m WHERE pedido_id=@p AND status='ativo'", ("p", pedidoId), ("m", motivo));
        return await c.ExecuteNonQueryAsync();
    }

    public Task<int> Revogar(long acessoId, string motivo) =>
        banco.Executar("UPDATE acessos SET status='revogado', motivo=@m WHERE id=@id AND status='ativo'", ("id", acessoId), ("m", motivo));

    /// <summary>Liga à conta as compras feitas antes com o mesmo e-mail (só depois do e-mail confirmado).</summary>
    public async Task<int> VincularPendentes(Guid usuarioId, string emailNormalizado)
    {
        await banco.Executar("UPDATE pedidos SET usuario_id=@u WHERE usuario_id IS NULL AND email_normalizado=@e",
            ("u", usuarioId), ("e", emailNormalizado));
        return await banco.Executar("UPDATE acessos SET usuario_id=@u WHERE usuario_id IS NULL AND email_normalizado=@e",
            ("u", usuarioId), ("e", emailNormalizado));
    }

    /// <summary>Id da conta confirmada com este e-mail (ou nulo).</summary>
    public static async Task<Guid?> ContaConfirmada(NpgsqlConnection con, NpgsqlTransaction tx, string emailNormalizado)
    {
        await using var c = Banco.Comando(con, tx,
            "SELECT id FROM usuarios WHERE email_normalizado=@e AND email_confirmado AND excluido_em IS NULL", ("e", emailNormalizado));
        return await c.ExecuteScalarAsync() as Guid?;
    }
}
