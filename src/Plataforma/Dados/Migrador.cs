using System.Reflection;
using Npgsql;

namespace Plataforma.Dados;

/// <summary>
/// Aplica os arquivos Dados/Migracoes/*.sql que ainda não rodaram, em ordem de nome.
/// Cada arquivo roda numa transação: se der erro no meio, nada dele fica gravado.
/// </summary>
public static class Migrador
{
    public static async Task Aplicar(NpgsqlDataSource fonte, ILogger log)
    {
        await using var con = await fonte.OpenConnectionAsync();

        // Trava para duas cópias do sistema não migrarem ao mesmo tempo
        await using (var trava = new NpgsqlCommand("SELECT pg_advisory_lock(7201)", con)) await trava.ExecuteNonQueryAsync();
        try
        {
            await using (var cria = new NpgsqlCommand(
                "CREATE TABLE IF NOT EXISTS schema_migracoes (versao text PRIMARY KEY, aplicada_em timestamptz NOT NULL DEFAULT now())", con))
                await cria.ExecuteNonQueryAsync();

            var feitas = new HashSet<string>();
            await using (var ler = new NpgsqlCommand("SELECT versao FROM schema_migracoes", con))
            await using (var r = await ler.ExecuteReaderAsync())
                while (await r.ReadAsync()) feitas.Add(r.GetString(0));

            var asm = Assembly.GetExecutingAssembly();
            var recursos = asm.GetManifestResourceNames()
                .Where(n => n.Contains(".Migracoes.") && n.EndsWith(".sql"))
                .OrderBy(n => n, StringComparer.Ordinal);

            foreach (var recurso in recursos)
            {
                var versao = recurso[(recurso.IndexOf(".Migracoes.", StringComparison.Ordinal) + 11)..^4];
                if (feitas.Contains(versao)) continue;

                using var leitor = new StreamReader(asm.GetManifestResourceStream(recurso)!);
                var sql = await leitor.ReadToEndAsync();

                await using var tx = await con.BeginTransactionAsync();
                await using (var cmd = new NpgsqlCommand(sql, con, tx)) await cmd.ExecuteNonQueryAsync();
                await using (var reg = new NpgsqlCommand("INSERT INTO schema_migracoes (versao) VALUES (@v)", con, tx))
                {
                    reg.Parameters.AddWithValue("v", versao);
                    await reg.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
                log.LogInformation("Migração aplicada: {Versao}", versao);
            }
        }
        finally
        {
            await using var solta = new NpgsqlCommand("SELECT pg_advisory_unlock(7201)", con);
            await solta.ExecuteNonQueryAsync();
        }
    }
}
