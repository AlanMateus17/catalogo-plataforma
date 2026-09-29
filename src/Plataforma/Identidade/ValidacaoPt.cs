using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Plataforma.Identidade;

/// <summary>Mensagens do Identity em português.</summary>
public sealed class ErrosPt : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int n) => new() { Code = nameof(PasswordTooShort), Description = $"A senha precisa ter pelo menos {n} caracteres." };
    public override IdentityError PasswordRequiresUniqueChars(int n) => new() { Code = nameof(PasswordRequiresUniqueChars), Description = $"A senha precisa ter pelo menos {n} caracteres diferentes." };
    public override IdentityError DuplicateEmail(string e) => new() { Code = nameof(DuplicateEmail), Description = "Este e-mail já tem conta." };
    public override IdentityError DuplicateUserName(string e) => new() { Code = nameof(DuplicateUserName), Description = "Este e-mail já tem conta." };
    public override IdentityError InvalidEmail(string? e) => new() { Code = nameof(InvalidEmail), Description = "E-mail inválido." };
    public override IdentityError InvalidToken() => new() { Code = nameof(InvalidToken), Description = "Link inválido ou vencido. Peça um novo." };
    public override IdentityError PasswordMismatch() => new() { Code = nameof(PasswordMismatch), Description = "Senha atual incorreta." };
    public override IdentityError ConcurrencyFailure() => new() { Code = nameof(ConcurrencyFailure), Description = "Os dados mudaram enquanto você editava. Recarregue e tente de novo." };
    public override IdentityError DefaultError() => new() { Code = nameof(DefaultError), Description = "Não foi possível concluir. Tente de novo." };
}

/// <summary>
/// Regras de senha além do tamanho: recusa as senhas mais usadas e senhas
/// que contêm o próprio e-mail ou nome. (Recomendação do NIST: senha longa
/// e fora de listas vazadas vale mais que exigir símbolo e maiúscula.)
/// </summary>
public sealed class ValidadorSenhaComum : IPasswordValidator<Usuario>
{
    static readonly HashSet<string> Comuns = new(StringComparer.OrdinalIgnoreCase)
    {
        "1234567890","12345678910","123456789a","0123456789","9876543210","1111111111","0000000000","1234512345",
        "qwertyuiop","asdfghjkla","qwerty1234","senha12345","senhasenha","minhasenha","mudar12345","trocar1234",
        "password12","password123","passw0rd12","iloveyou12","abc1234567","abcdefghij","brasil1234","brasil2024",
        "brasil2025","brasil2026","flamengo10","corinthians","palmeiras1","saopaulo10","cruzeiro10","atletico10",
        "123mudar123","admin12345","administrador","aluno12345","professor1","escola1234","estudante1","catalogo12",
    };

    public Task<IdentityResult> ValidateAsync(UserManager<Usuario> gerente, Usuario u, string? senha)
    {
        senha ??= "";
        var erros = new List<IdentityError>();
        var baixa = senha.ToLowerInvariant();
        var local = (u.Email.Split('@')[0]).ToLowerInvariant();
        var primeiroNome = u.Nome.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? "";

        if (Comuns.Contains(senha) || senha.Distinct().Count() <= 2)
            erros.Add(new() { Code = "SenhaComum", Description = "Essa senha é muito usada. Escolha outra (uma frase curta funciona bem)." });
        if (local.Length >= 4 && baixa.Contains(local))
            erros.Add(new() { Code = "SenhaComEmail", Description = "A senha não pode conter o seu e-mail." });
        if (primeiroNome.Length >= 4 && baixa.Contains(primeiroNome) && senha.Length < 16)
            erros.Add(new() { Code = "SenhaComNome", Description = "A senha não pode ser baseada no seu nome." });

        return Task.FromResult(erros.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. erros]));
    }
}

/// <summary>
/// Monta o "crachá" (cookie) do usuário: id, e-mail, nome, papéis e se tem 2 etapas.
/// As permissões são calculadas a partir dos papéis a cada pedido (Permissoes.DoUsuario).
/// </summary>
public sealed class FabricaCracha(UserManager<Usuario> gerente, RoleManager<Papel> papeis, IOptions<IdentityOptions> opcoes)
    : UserClaimsPrincipalFactory<Usuario, Papel>(gerente, papeis, opcoes)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(Usuario u)
    {
        var id = await base.GenerateClaimsAsync(u);
        id.AddClaim(new Claim("nome", u.Nome));
        id.AddClaim(new Claim("dois_fatores", u.DoisFatores ? "sim" : "nao"));
        return id;
    }
}

public static class Emails
{
    /// <summary>
    /// Expressão regular do PostgreSQL (use com ~*) que acha ESTE e-mail inteiro num texto:
    /// "ana@x.com" não casa com "joana@x.com" nem com "ana@x.com.br".
    /// </summary>
    public static string PadraoExato(string email)
    {
        var e = string.Concat(email.Trim().Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch.ToString() : "\\" + ch));
        return "(^|[^a-z0-9._%+-])" + e + "($|[^a-z0-9.-]|\\.$|\\.[^a-z0-9])";
    }

    public static string Normalizar(string email) => email.Trim().ToUpperInvariant();

    public static bool Valido(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254) return false;
        try
        {
            var m = new System.Net.Mail.MailAddress(email.Trim());
            return m.Address == email.Trim() && m.Host.Contains('.');
        }
        catch { return false; }
    }
}
