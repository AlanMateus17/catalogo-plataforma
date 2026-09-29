using Plataforma.Dados;

namespace Plataforma.Vendas;

/// <summary>
/// Uma vez por hora: pedidos do Mercado Pago que ficaram "pendente" por mais de 25 horas
/// (o link de pagamento vence em 24 h) viram "expirado", e o uso do cupom reservado volta.
/// Sem isso, alguém poderia abrir checkouts sem pagar só para esgotar um cupom.
/// </summary>
public sealed class ExpirarPedidos(Banco banco, ILogger<ExpirarPedidos> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken parar)
    {
        using var relogio = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                var n = await banco.Transacao(async (con, tx) =>
                {
                    await using var c = Banco.Comando(con, tx, """
                        WITH vencidos AS (
                            UPDATE pedidos SET status='expirado', atualizado_em=now()
                            WHERE plataforma='mercadopago' AND status='pendente' AND criado_em < now() - interval '25 hours'
                            RETURNING cupom_id
                        ), devolve AS (
                            UPDATE cupons SET usos = greatest(usos - v.qtd, 0)
                            FROM (SELECT cupom_id, count(*) AS qtd FROM vencidos WHERE cupom_id IS NOT NULL GROUP BY cupom_id) v
                            WHERE cupons.id = v.cupom_id
                        )
                        SELECT count(*) FROM vencidos
                        """);
                    return Convert.ToInt32(await c.ExecuteScalarAsync());
                });
                if (n > 0) log.LogInformation("{N} pedido(s) do Mercado Pago expiraram sem pagamento", n);
            }
            catch (Exception ex) when (!parar.IsCancellationRequested) { log.LogError(ex, "Falha ao expirar pedidos"); }
        } while (await relogio.WaitForNextTickAsync(parar));
    }
}
