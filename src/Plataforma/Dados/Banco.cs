using System.Data.Common;
using Npgsql;

namespace Plataforma.Dados;

/// <summary>
/// Acesso ao PostgreSQL com SQL escrito à mão (sem ORM).
/// Todo valor entra por parâmetro (@nome), nunca concatenado no texto do SQL:
/// é isso que impede SQL injection.
/// </summary>
public sealed class Banco(NpgsqlDataSource fonte)
{
    public NpgsqlDataSource Fonte => fonte;

    public async Task<int> Executar(string sql, params (string nome, object? valor)[] parametros)
    {
        await using var cmd = Comando(fonte.CreateCommand(sql), parametros);
        return await cmd.ExecuteNonQueryAsync();
    }

    public async Task<T?> Escalar<T>(string sql, params (string nome, object? valor)[] parametros)
    {
        await using var cmd = Comando(fonte.CreateCommand(sql), parametros);
        var r = await cmd.ExecuteScalarAsync();
        return r is null or DBNull ? default : (T)Convert.ChangeType(r, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    public async Task<List<T>> Lista<T>(string sql, Func<DbDataReader, T> ler, params (string nome, object? valor)[] parametros)
    {
        await using var cmd = Comando(fonte.CreateCommand(sql), parametros);
        await using var r = await cmd.ExecuteReaderAsync();
        var lista = new List<T>();
        while (await r.ReadAsync()) lista.Add(ler(r));
        return lista;
    }

    public async Task<T?> Um<T>(string sql, Func<DbDataReader, T> ler, params (string nome, object? valor)[] parametros) where T : class
    {
        var lista = await Lista(sql, ler, parametros);
        return lista.Count > 0 ? lista[0] : null;
    }

    /// <summary>Roda várias operações numa transação: ou tudo grava, ou nada grava.</summary>
    public async Task<T> Transacao<T>(Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> trabalho)
    {
        await using var con = await fonte.OpenConnectionAsync();
        await using var tx = await con.BeginTransactionAsync();
        var resultado = await trabalho(con, tx);
        await tx.CommitAsync();
        return resultado;
    }

    public static NpgsqlCommand Comando(NpgsqlCommand cmd, (string nome, object? valor)[] parametros)
    {
        foreach (var (nome, valor) in parametros)
            cmd.Parameters.AddWithValue(nome, valor ?? DBNull.Value);
        return cmd;
    }

    public static NpgsqlCommand Comando(NpgsqlConnection con, NpgsqlTransaction tx, string sql, params (string nome, object? valor)[] parametros)
        => Comando(new NpgsqlCommand(sql, con, tx), parametros);
}

/// <summary>Atalhos para ler colunas que podem ser NULL.</summary>
public static class LeitorExtensoes
{
    public static string? TextoOuNulo(this DbDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : r.GetString(i); }
    public static string Texto(this DbDataReader r, string col) => r.GetString(r.GetOrdinal(col));
    public static int Inteiro(this DbDataReader r, string col) => r.GetInt32(r.GetOrdinal(col));
    public static int? InteiroOuNulo(this DbDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : r.GetInt32(i); }
    public static long Longo(this DbDataReader r, string col) => r.GetInt64(r.GetOrdinal(col));
    public static bool Logico(this DbDataReader r, string col) => r.GetBoolean(r.GetOrdinal(col));
    public static Guid Id(this DbDataReader r, string col) => r.GetGuid(r.GetOrdinal(col));
    public static Guid? IdOuNulo(this DbDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : r.GetGuid(i); }
    public static DateTimeOffset? DataOuNula(this DbDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(i), DateTimeKind.Utc));
    }
    public static DateTimeOffset Data(this DbDataReader r, string col) => r.DataOuNula(col)!.Value;
    public static string[] Textos(this DbDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? [] : r.GetFieldValue<string[]>(i); }
    public static int[]? Inteiros(this DbDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : r.GetFieldValue<int[]>(i); }
}
