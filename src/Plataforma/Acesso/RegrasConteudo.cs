using System.Text.RegularExpressions;
using Plataforma.Dados;

namespace Plataforma.Acesso;

public sealed record Regra(int Id, string Padrao, string Nivel, string? Liberacao, int Ordem, string Descricao)
{
    public Regex Expressao { get; } = new("^" + Regex.Escape(Padrao).Replace("\\*", ".*") + "$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
}

/// <summary>
/// Lê as regras de conteúdo do banco e guarda em memória (recarrega quando alguém muda no painel).
/// A primeira regra (menor "ordem") que combina com o caminho decide. Sem regra = não serve.
/// </summary>
public sealed class RegrasConteudo(Banco banco)
{
    volatile List<Regra>? _cache;

    public async Task<List<Regra>> Todas() => _cache ??= await banco.Lista(
        "SELECT id, padrao, nivel, liberacao, ordem, descricao FROM regras_conteudo ORDER BY ordem, id",
        r => new Regra(r.Inteiro("id"), r.Texto("padrao"), r.Texto("nivel"), r.TextoOuNulo("liberacao"), r.Inteiro("ordem"), r.Texto("descricao")));

    public void Recarregar() => _cache = null;

    public async Task<Regra?> Encontrar(string caminho)
    {
        foreach (var r in await Todas())
            if (r.Expressao.IsMatch(caminho)) return r;
        return null;
    }
}
