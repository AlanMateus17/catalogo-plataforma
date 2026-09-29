using System.Data.Common;
using Microsoft.AspNetCore.Identity;
using Plataforma.Dados;

namespace Plataforma.Identidade;

/// <summary>
/// Liga o ASP.NET Core Identity (hash de senha, bloqueio por tentativas, tokens de e-mail,
/// verificação em duas etapas) à tabela usuarios do PostgreSQL, com SQL escrito à mão.
/// O Identity chama estes métodos; quem decide as regras é ele.
/// </summary>
public sealed class UsuarioStore(Banco banco) :
    IUserPasswordStore<Usuario>, IUserEmailStore<Usuario>, IUserSecurityStampStore<Usuario>,
    IUserLockoutStore<Usuario>, IUserRoleStore<Usuario>, IUserTwoFactorStore<Usuario>,
    IUserAuthenticatorKeyStore<Usuario>, IUserTwoFactorRecoveryCodeStore<Usuario>, IUserPhoneNumberStore<Usuario>
{
    const string Colunas = """
        id, nome, email, email_normalizado, email_confirmado, senha_hash, carimbo_seguranca, carimbo_concorrencia,
        bloqueio_ativo, bloqueio_ate, falhas_acesso, dois_fatores, chave_autenticador, codigos_recuperacao,
        bloqueado_por_admin, motivo_bloqueio, termos_versao, termos_aceitos_em, teste_usado_em, criado_em,
        ultimo_login_em, excluido_em
        """;

    public static Usuario Ler(DbDataReader r) => new()
    {
        Id = r.Id("id"), Nome = r.Texto("nome"), Email = r.Texto("email"), EmailNormalizado = r.Texto("email_normalizado"),
        EmailConfirmado = r.Logico("email_confirmado"), SenhaHash = r.TextoOuNulo("senha_hash"),
        CarimboSeguranca = r.Texto("carimbo_seguranca"), CarimboConcorrencia = r.Texto("carimbo_concorrencia"),
        BloqueioAtivo = r.Logico("bloqueio_ativo"), BloqueioAte = r.DataOuNula("bloqueio_ate"), FalhasAcesso = r.Inteiro("falhas_acesso"),
        DoisFatores = r.Logico("dois_fatores"), ChaveAutenticador = r.TextoOuNulo("chave_autenticador"),
        CodigosRecuperacao = r.TextoOuNulo("codigos_recuperacao"), BloqueadoPorAdmin = r.Logico("bloqueado_por_admin"),
        MotivoBloqueio = r.TextoOuNulo("motivo_bloqueio"), TermosVersao = r.TextoOuNulo("termos_versao"),
        TermosAceitosEm = r.DataOuNula("termos_aceitos_em"), TesteUsadoEm = r.DataOuNula("teste_usado_em"),
        CriadoEm = r.Data("criado_em"), UltimoLoginEm = r.DataOuNula("ultimo_login_em"), ExcluidoEm = r.DataOuNula("excluido_em"),
    };

    static (string, object?)[] Parametros(Usuario u) =>
    [
        ("id", u.Id), ("nome", u.Nome), ("email", u.Email), ("email_normalizado", u.EmailNormalizado),
        ("email_confirmado", u.EmailConfirmado), ("senha_hash", u.SenhaHash), ("carimbo_seguranca", u.CarimboSeguranca),
        ("bloqueio_ativo", u.BloqueioAtivo), ("bloqueio_ate", u.BloqueioAte?.ToUniversalTime()), ("falhas_acesso", u.FalhasAcesso),
        ("dois_fatores", u.DoisFatores), ("chave_autenticador", u.ChaveAutenticador), ("codigos_recuperacao", u.CodigosRecuperacao),
        ("bloqueado_por_admin", u.BloqueadoPorAdmin), ("motivo_bloqueio", u.MotivoBloqueio), ("termos_versao", u.TermosVersao),
        ("termos_aceitos_em", u.TermosAceitosEm?.ToUniversalTime()), ("teste_usado_em", u.TesteUsadoEm?.ToUniversalTime()),
        ("ultimo_login_em", u.UltimoLoginEm?.ToUniversalTime()), ("excluido_em", u.ExcluidoEm?.ToUniversalTime()),
    ];

    // ---------- IUserStore ----------
    public async Task<IdentityResult> CreateAsync(Usuario u, CancellationToken ct)
    {
        u.CarimboConcorrencia = Guid.NewGuid().ToString("N");
        try
        {
            await banco.Executar($"""
                INSERT INTO usuarios ({Colunas})
                VALUES (@id, @nome, @email, @email_normalizado, @email_confirmado, @senha_hash, @carimbo_seguranca, @carimbo_concorrencia,
                        @bloqueio_ativo, @bloqueio_ate, @falhas_acesso, @dois_fatores, @chave_autenticador, @codigos_recuperacao,
                        @bloqueado_por_admin, @motivo_bloqueio, @termos_versao, @termos_aceitos_em, @teste_usado_em, now(),
                        @ultimo_login_em, @excluido_em)
                """, [.. Parametros(u), ("carimbo_concorrencia", u.CarimboConcorrencia)]);
            return IdentityResult.Success;
        }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505")
        {
            return IdentityResult.Failed(new IdentityError { Code = "DuplicateEmail", Description = "E-mail já cadastrado." });
        }
    }

    public async Task<IdentityResult> UpdateAsync(Usuario u, CancellationToken ct)
    {
        var novo = Guid.NewGuid().ToString("N");
        var linhas = await banco.Executar("""
            UPDATE usuarios SET nome=@nome, email=@email, email_normalizado=@email_normalizado, email_confirmado=@email_confirmado,
                senha_hash=@senha_hash, carimbo_seguranca=@carimbo_seguranca, carimbo_concorrencia=@novo,
                bloqueio_ativo=@bloqueio_ativo, bloqueio_ate=@bloqueio_ate, falhas_acesso=@falhas_acesso, dois_fatores=@dois_fatores,
                chave_autenticador=@chave_autenticador, codigos_recuperacao=@codigos_recuperacao,
                bloqueado_por_admin=@bloqueado_por_admin, motivo_bloqueio=@motivo_bloqueio, termos_versao=@termos_versao,
                termos_aceitos_em=@termos_aceitos_em, teste_usado_em=@teste_usado_em, ultimo_login_em=@ultimo_login_em,
                excluido_em=@excluido_em
            WHERE id=@id AND carimbo_concorrencia=@carimbo_concorrencia
            """, [.. Parametros(u), ("carimbo_concorrencia", u.CarimboConcorrencia), ("novo", novo)]);
        if (linhas == 0)
            return IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());
        u.CarimboConcorrencia = novo;
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(Usuario u, CancellationToken ct)
    {
        await banco.Executar("DELETE FROM usuarios WHERE id=@id", ("id", u.Id));
        return IdentityResult.Success;
    }

    public Task<Usuario?> FindByIdAsync(string id, CancellationToken ct) =>
        Guid.TryParse(id, out var g)
            ? banco.Um($"SELECT {Colunas} FROM usuarios WHERE id=@id", Ler, ("id", g))
            : Task.FromResult<Usuario?>(null);

    // O "nome de usuário" do Identity é o e-mail.
    public Task<Usuario?> FindByNameAsync(string nomeNormalizado, CancellationToken ct) => FindByEmailAsync(nomeNormalizado, ct);
    public Task<string> GetUserIdAsync(Usuario u, CancellationToken ct) => Task.FromResult(u.Id.ToString());
    public Task<string?> GetUserNameAsync(Usuario u, CancellationToken ct) => Task.FromResult<string?>(u.Email);
    public Task SetUserNameAsync(Usuario u, string? nome, CancellationToken ct) => Task.CompletedTask;
    public Task<string?> GetNormalizedUserNameAsync(Usuario u, CancellationToken ct) => Task.FromResult<string?>(u.EmailNormalizado);
    public Task SetNormalizedUserNameAsync(Usuario u, string? nome, CancellationToken ct) => Task.CompletedTask;

    // ---------- Senha ----------
    public Task SetPasswordHashAsync(Usuario u, string? hash, CancellationToken ct) { u.SenhaHash = hash; return Task.CompletedTask; }
    public Task<string?> GetPasswordHashAsync(Usuario u, CancellationToken ct) => Task.FromResult(u.SenhaHash);
    public Task<bool> HasPasswordAsync(Usuario u, CancellationToken ct) => Task.FromResult(u.SenhaHash is not null);

    // ---------- E-mail ----------
    public Task SetEmailAsync(Usuario u, string? email, CancellationToken ct) { u.Email = email ?? ""; return Task.CompletedTask; }
    public Task<string?> GetEmailAsync(Usuario u, CancellationToken ct) => Task.FromResult<string?>(u.Email);
    public Task<bool> GetEmailConfirmedAsync(Usuario u, CancellationToken ct) => Task.FromResult(u.EmailConfirmado);
    public Task SetEmailConfirmedAsync(Usuario u, bool c, CancellationToken ct) { u.EmailConfirmado = c; return Task.CompletedTask; }
    public Task<Usuario?> FindByEmailAsync(string emailNormalizado, CancellationToken ct) =>
        banco.Um($"SELECT {Colunas} FROM usuarios WHERE email_normalizado=@e AND excluido_em IS NULL", Ler, ("e", emailNormalizado));
    public Task<string?> GetNormalizedEmailAsync(Usuario u, CancellationToken ct) => Task.FromResult<string?>(u.EmailNormalizado);
    public Task SetNormalizedEmailAsync(Usuario u, string? e, CancellationToken ct) { u.EmailNormalizado = e ?? ""; return Task.CompletedTask; }

    // ---------- Carimbo de segurança ----------
    public Task SetSecurityStampAsync(Usuario u, string stamp, CancellationToken ct) { u.CarimboSeguranca = stamp; return Task.CompletedTask; }
    public Task<string?> GetSecurityStampAsync(Usuario u, CancellationToken ct) => Task.FromResult<string?>(u.CarimboSeguranca);

    // ---------- Bloqueio ----------
    public Task<DateTimeOffset?> GetLockoutEndDateAsync(Usuario u, CancellationToken ct) => Task.FromResult(u.BloqueioAte);
    public Task SetLockoutEndDateAsync(Usuario u, DateTimeOffset? fim, CancellationToken ct) { u.BloqueioAte = fim; return Task.CompletedTask; }
    public Task<int> IncrementAccessFailedCountAsync(Usuario u, CancellationToken ct) => Task.FromResult(++u.FalhasAcesso);
    public Task ResetAccessFailedCountAsync(Usuario u, CancellationToken ct) { u.FalhasAcesso = 0; return Task.CompletedTask; }
    public Task<int> GetAccessFailedCountAsync(Usuario u, CancellationToken ct) => Task.FromResult(u.FalhasAcesso);
    public Task<bool> GetLockoutEnabledAsync(Usuario u, CancellationToken ct) => Task.FromResult(u.BloqueioAtivo);
    public Task SetLockoutEnabledAsync(Usuario u, bool ativo, CancellationToken ct) { u.BloqueioAtivo = ativo; return Task.CompletedTask; }

    // ---------- Papéis ----------
    public async Task AddToRoleAsync(Usuario u, string papelNormalizado, CancellationToken ct) =>
        await banco.Executar("INSERT INTO usuario_papeis (usuario_id, papel_id) VALUES (@u, lower(@p)) ON CONFLICT DO NOTHING",
            ("u", u.Id), ("p", papelNormalizado));
    public async Task RemoveFromRoleAsync(Usuario u, string papelNormalizado, CancellationToken ct) =>
        await banco.Executar("DELETE FROM usuario_papeis WHERE usuario_id=@u AND papel_id=lower(@p)", ("u", u.Id), ("p", papelNormalizado));
    public async Task<IList<string>> GetRolesAsync(Usuario u, CancellationToken ct) =>
        await banco.Lista("SELECT papel_id FROM usuario_papeis WHERE usuario_id=@u ORDER BY papel_id", r => r.GetString(0), ("u", u.Id));
    public async Task<bool> IsInRoleAsync(Usuario u, string papelNormalizado, CancellationToken ct) =>
        await banco.Escalar<bool>("SELECT EXISTS (SELECT 1 FROM usuario_papeis WHERE usuario_id=@u AND papel_id=lower(@p))",
            ("u", u.Id), ("p", papelNormalizado));
    public async Task<IList<Usuario>> GetUsersInRoleAsync(string papelNormalizado, CancellationToken ct) =>
        await banco.Lista($"SELECT {Colunas} FROM usuarios WHERE id IN (SELECT usuario_id FROM usuario_papeis WHERE papel_id=lower(@p))",
            Ler, ("p", papelNormalizado));

    // ---------- Verificação em duas etapas ----------
    public Task SetTwoFactorEnabledAsync(Usuario u, bool ativo, CancellationToken ct) { u.DoisFatores = ativo; return Task.CompletedTask; }
    public Task<bool> GetTwoFactorEnabledAsync(Usuario u, CancellationToken ct) => Task.FromResult(u.DoisFatores);
    public Task SetAuthenticatorKeyAsync(Usuario u, string chave, CancellationToken ct) { u.ChaveAutenticador = chave; return Task.CompletedTask; }
    public Task<string?> GetAuthenticatorKeyAsync(Usuario u, CancellationToken ct) => Task.FromResult(u.ChaveAutenticador);
    public Task ReplaceCodesAsync(Usuario u, IEnumerable<string> codigos, CancellationToken ct)
    { u.CodigosRecuperacao = string.Join(";", codigos.Select(Resumo)); return Task.CompletedTask; }
    public Task<bool> RedeemCodeAsync(Usuario u, string codigo, CancellationToken ct)
    {
        var lista = (u.CodigosRecuperacao ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!lista.Remove(Resumo(codigo))) return Task.FromResult(false);
        u.CodigosRecuperacao = string.Join(";", lista);
        return Task.FromResult(true);
    }
    public Task<int> CountCodesAsync(Usuario u, CancellationToken ct) =>
        Task.FromResult((u.CodigosRecuperacao ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).Length);

    // ---------- Telefone: não usamos (o código por SMS é o método mais fraco de 2 etapas) ----------
    public Task SetPhoneNumberAsync(Usuario u, string? t, CancellationToken ct) => Task.CompletedTask;
    public Task<string?> GetPhoneNumberAsync(Usuario u, CancellationToken ct) => Task.FromResult<string?>(null);
    public Task<bool> GetPhoneNumberConfirmedAsync(Usuario u, CancellationToken ct) => Task.FromResult(false);
    public Task SetPhoneNumberConfirmedAsync(Usuario u, bool c, CancellationToken ct) => Task.CompletedTask;

    // Códigos de recuperação ficam guardados só como resumo (SHA-256), como uma senha.
    static string Resumo(string codigo) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(codigo.Trim().ToUpperInvariant())));

    public void Dispose() { }
}

/// <summary>Papéis são fixos (tabela papeis); o Identity só precisa conseguir encontrá-los.</summary>
public sealed class PapelStore(Banco banco) : IRoleStore<Papel>
{
    static Papel Ler(DbDataReader r) => new() { Id = r.Texto("id"), Nome = r.Texto("nome") };
    public Task<IdentityResult> CreateAsync(Papel p, CancellationToken ct) => Task.FromResult(IdentityResult.Failed(new IdentityError { Description = "Papéis são fixos." }));
    public Task<IdentityResult> UpdateAsync(Papel p, CancellationToken ct) => Task.FromResult(IdentityResult.Failed(new IdentityError { Description = "Papéis são fixos." }));
    public Task<IdentityResult> DeleteAsync(Papel p, CancellationToken ct) => Task.FromResult(IdentityResult.Failed(new IdentityError { Description = "Papéis são fixos." }));
    public Task<string> GetRoleIdAsync(Papel p, CancellationToken ct) => Task.FromResult(p.Id);
    public Task<string?> GetRoleNameAsync(Papel p, CancellationToken ct) => Task.FromResult<string?>(p.Id);
    public Task SetRoleNameAsync(Papel p, string? n, CancellationToken ct) => Task.CompletedTask;
    public Task<string?> GetNormalizedRoleNameAsync(Papel p, CancellationToken ct) => Task.FromResult<string?>(p.Id.ToUpperInvariant());
    public Task SetNormalizedRoleNameAsync(Papel p, string? n, CancellationToken ct) => Task.CompletedTask;
    public Task<Papel?> FindByIdAsync(string id, CancellationToken ct) => banco.Um("SELECT id, nome FROM papeis WHERE id=@id", Ler, ("id", id));
    public Task<Papel?> FindByNameAsync(string nome, CancellationToken ct) => banco.Um("SELECT id, nome FROM papeis WHERE id=lower(@n)", Ler, ("n", nome));
    public void Dispose() { }
}
