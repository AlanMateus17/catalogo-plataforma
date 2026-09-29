using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Plataforma.Acesso;
using Plataforma.Auditoria;
using Plataforma.Dados;
using Plataforma.Email;
using Plataforma.Identidade;
using Plataforma.Seguranca;
using Plataforma.Vendas;

namespace Plataforma.Api;

/// <summary>Tudo o que o aluno faz com a própria conta: cadastro, login, senha, 2 etapas, dados.</summary>
[ApiController, Route("api/conta"), ValidarCsrf]
public sealed class ContaController(
    UserManager<Usuario> usuarios, SignInManager<Usuario> entrada, ServicoAcesso acesso, ServicoVendas vendas,
    IEnviadorEmail email, ModelosEmail modelos, RegistroAuditoria auditoria, Banco banco, IConfiguration config,
    IAntiforgery antiforgery, ServicoContas contas) : ControllerBase
{
    public const string VersaoTermos = "2026-10";

    string UrlPublica => (config["Site:UrlPublica"] ?? $"{Request.Scheme}://{Request.Host}").TrimEnd('/');
    static string Cod(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    static string? Decod(string? c) { try { return c is null ? null : Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(c)); } catch { return null; } }
    static ObjectResult Erro(string msg, string? codigo = null, int status = 400) => new(new { erro = msg, codigo }) { StatusCode = status };
    static ObjectResult Erro(IdentityResult r) => Erro(string.Join(" ", r.Errors.Select(e => e.Description)), "validacao");

    async Task<Usuario?> Atual() => await usuarios.GetUserAsync(User);

    // ---------------------------------------------------------------- CSRF
    [HttpGet("csrf")]
    public IActionResult Csrf()
    {
        var t = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Headers.CacheControl = "no-store";
        return Ok(new { token = t.RequestToken });
    }

    // ---------------------------------------------------------------- Quem sou eu
    [HttpGet("eu")]
    public async Task<IActionResult> Eu()
    {
        Response.Headers.CacheControl = "no-store";
        if (User.Identity?.IsAuthenticated != true) return Ok(new { logado = false });
        var u = await Atual();
        if (u is null) return Ok(new { logado = false });

        var papeis = await usuarios.GetRolesAsync(u);
        var libs = await acesso.Liberacoes(u.Id);
        var equipe = papeis.Any(p => Permissoes.PapeisEquipe.Contains(p));
        return Ok(new
        {
            logado = true,
            id = u.Id,
            nome = u.Nome,
            email = u.Email,
            papeis,
            permissoes = Permissoes.DoUsuario(User),
            equipe,
            precisaAtivar2fa = equipe && !u.DoisFatores && config.GetValue("Seguranca:Exigir2faEquipe", true),
            doisFatores = u.DoisFatores,
            liberacoes = libs,
            acessos = await acesso.DoUsuario(u.Id),
            testeDisponivel = u.TesteUsadoEm is null && libs.Count == 0,
            criadoEm = u.CriadoEm,
        });
    }

    // ---------------------------------------------------------------- Cadastro
    public sealed record CadastroPedido(string? Nome, string? Email, string? Senha, bool AceitaTermos);

    [HttpPost("cadastro"), EnableRateLimiting("autenticacao")]
    public async Task<IActionResult> Cadastro(CadastroPedido p)
    {
        var nome = (p.Nome ?? "").Trim();
        var mail = (p.Email ?? "").Trim();
        if (nome.Length is < 2 or > 120) return Erro("Informe seu nome (de 2 a 120 letras).", "nome");
        if (!Emails.Valido(mail)) return Erro("Informe um e-mail válido.", "email");
        if (!p.AceitaTermos) return Erro("Para criar a conta, aceite os Termos de Uso e a Política de Privacidade.", "termos");

        const string resposta = "Enviamos um link de confirmação para o seu e-mail. Abra-o para ativar a conta (confira também o spam).";
        var existente = await usuarios.FindByEmailAsync(mail);
        if (existente is not null)
        {
            // Mesma resposta para quem já tem conta: ninguém descobre quais e-mails estão cadastrados.
            if (existente.EmailConfirmado)
            {
                var (a, h) = modelos.JaTemConta(existente.Nome);
                await email.Enviar(existente.Email, a, h);
            }
            else
            {
                // Conta ainda não ativada: vale o nome do último cadastro (quem ativa pelo e-mail é a dona dele)
                existente.Nome = nome; existente.TermosVersao = VersaoTermos; existente.TermosAceitosEm = DateTimeOffset.UtcNow;
                await usuarios.UpdateAsync(existente);
                await EnviarConfirmacao(existente);
            }
            return Ok(new { ok = true, mensagem = resposta });
        }

        var u = new Usuario { Nome = nome, Email = mail, TermosVersao = VersaoTermos, TermosAceitosEm = DateTimeOffset.UtcNow };
        // A senha é criada na confirmação do e-mail: só quem abre o e-mail define a senha da conta.
        var r = await usuarios.CreateAsync(u);
        if (!r.Succeeded) return Erro(r);
        await usuarios.AddToRoleAsync(u, "aluno");
        await auditoria.Registrar("conta.criada", u.Email, usuarioId: u.Id, ator: u.Email);
        await EnviarConfirmacao(u);
        return Ok(new { ok = true, mensagem = resposta });
    }

    async Task EnviarConfirmacao(Usuario u)
    {
        var token = await usuarios.GenerateEmailConfirmationTokenAsync(u);
        var url = $"{UrlPublica}/confirmar-email.html?id={u.Id}&codigo={Cod(token)}";
        var (a, h) = modelos.Confirmacao(u.Nome, url);
        await email.Enviar(u.Email, a, h);
    }

    public sealed record ConfirmarPedido(string? Id, string? Codigo, string? Senha);

    /// <summary>
    /// Confirma o e-mail. Quem tem o link (a dona do e-mail) define a senha AQUI:
    /// se outra pessoa tiver criado a conta antes com este e-mail, a senha dela deixa de valer.
    /// Trocar a senha muda o carimbo de segurança, então o link só funciona uma vez.
    /// </summary>
    [HttpPost("confirmar-email"), EnableRateLimiting("autenticacao")]
    public async Task<IActionResult> ConfirmarEmail(ConfirmarPedido p)
    {
        const string invalido = "Link inválido, vencido ou já usado. Peça um novo na tela de entrada.";
        var u = p.Id is null ? null : await usuarios.FindByIdAsync(p.Id);
        var token = Decod(p.Codigo);
        if (u is null || token is null || u.BloqueadoPorAdmin) return Erro(invalido, "link");
        // O código é conferido SEMPRE, inclusive em conta já confirmada.
        var tokenOk = await usuarios.VerifyUserTokenAsync(u, usuarios.Options.Tokens.EmailConfirmationTokenProvider,
            UserManager<Usuario>.ConfirmEmailTokenPurpose, token);
        if (!tokenOk) return Erro(invalido, "link");
        if (u.EmailConfirmado) return Erro("Este e-mail já foi confirmado. Entre com sua senha.", "ja-confirmado");

        // Valida a senha antes de mudar qualquer coisa
        foreach (var v in usuarios.PasswordValidators)
        {
            var rv = await v.ValidateAsync(usuarios, u, p.Senha ?? "");
            if (!rv.Succeeded) return Erro(rv);
        }
        var r = await usuarios.ConfirmEmailAsync(u, token);
        if (!r.Succeeded) return Erro(invalido, "link");
        u = (await usuarios.FindByIdAsync(p.Id!))!;
        var reset = await usuarios.GeneratePasswordResetTokenAsync(u);
        var rs = await usuarios.ResetPasswordAsync(u, reset, p.Senha!);   // troca a senha e o carimbo de segurança
        if (!rs.Succeeded) return Erro(rs);
        u = (await usuarios.FindByIdAsync(p.Id!))!;
        await usuarios.SetLockoutEndDateAsync(u, null);
        await usuarios.ResetAccessFailedCountAsync(u);
        await auditoria.Registrar("conta.email-confirmado", u.Email, usuarioId: u.Id, ator: u.Email);

        var vinculados = await acesso.VincularPendentes(u.Id, u.EmailNormalizado);
        await contas.GarantirAdminInicial(u);
        await entrada.SignInAsync(u, isPersistent: false);   // conta recém-confirmada ainda não tem 2 etapas
        await banco.Executar("UPDATE usuarios SET ultimo_login_em = now() WHERE id=@id", ("id", u.Id));
        return Ok(new { ok = true, logado = true, comprasLiberadas = vinculados });
    }

    public sealed record EmailPedido(string? Email);

    [HttpPost("reenviar-confirmacao"), EnableRateLimiting("autenticacao")]
    public async Task<IActionResult> ReenviarConfirmacao(EmailPedido p)
    {
        var u = Emails.Valido(p.Email) ? await usuarios.FindByEmailAsync(p.Email!) : null;
        if (u is not null && !u.EmailConfirmado) await EnviarConfirmacao(u);
        return Ok(new { ok = true, mensagem = "Se houver uma conta aguardando confirmação com esse e-mail, enviamos um novo link." });
    }

    // ---------------------------------------------------------------- Entrar e sair
    public sealed record EntrarPedido(string? Email, string? Senha, bool Lembrar);

    [HttpPost("entrar"), EnableRateLimiting("autenticacao")]
    public async Task<IActionResult> Entrar(EntrarPedido p)
    {
        // A mesma resposta para e-mail inexistente, senha errada e conta travada por tentativas:
        // ninguém descobre quais e-mails estão cadastrados.
        const string generico = "E-mail ou senha incorretos. Depois de 5 tentativas erradas, a entrada fica travada por 15 minutos.";
        var u = Emails.Valido(p.Email) ? await usuarios.FindByEmailAsync(p.Email!) : null;
        if (u is null)
        {
            // Gasta o mesmo tempo de uma senha de verdade: o tempo de resposta não revela se o e-mail existe.
            usuarios.PasswordHasher.HashPassword(new Usuario(), p.Senha ?? "x");
            return Erro(generico, "credenciais", 401);
        }

        if (u.BloqueadoPorAdmin)
        {
            // Só conta que a conta está bloqueada para quem acertou a senha.
            var senhaCerta = await usuarios.CheckPasswordAsync(u, p.Senha ?? "");
            if (!senhaCerta) await usuarios.AccessFailedAsync(u);
            await auditoria.Registrar("login.bloqueado", u.Email, usuarioId: u.Id, ator: u.Email);
            return senhaCerta ? Erro("Esta conta está bloqueada. Fale com o suporte.", "bloqueada", 403) : Erro(generico, "credenciais", 401);
        }

        var check = await entrada.CheckPasswordSignInAsync(u, p.Senha ?? "", lockoutOnFailure: true);
        if (check.IsLockedOut)
        {
            usuarios.PasswordHasher.HashPassword(new Usuario(), p.Senha ?? "x");
            await auditoria.Registrar("login.travado", u.Email, usuarioId: u.Id, ator: u.Email);
            return Erro(generico, "credenciais", 401);
        }
        if (!check.Succeeded)
        {
            await auditoria.Registrar("login.falhou", u.Email, usuarioId: u.Id, ator: u.Email);
            return Erro(generico, "credenciais", 401);
        }
        if (!u.EmailConfirmado)
            return Erro("Confirme seu e-mail antes de entrar. Não achou o link? Peça outro abaixo.", "email-nao-confirmado", 403);

        var r = await entrada.PasswordSignInAsync(u, p.Senha ?? "", p.Lembrar, lockoutOnFailure: true);
        if (r.RequiresTwoFactor) return Ok(new { ok = false, precisa2fa = true });
        if (!r.Succeeded) return Erro(generico, "credenciais", 401);
        await AposEntrar(u);
        return Ok(new { ok = true });
    }

    async Task AposEntrar(Usuario u)
    {
        await banco.Executar("UPDATE usuarios SET ultimo_login_em = now() WHERE id=@id", ("id", u.Id));
        await acesso.VincularPendentes(u.Id, u.EmailNormalizado);
        await contas.GarantirAdminInicial(u);
        await auditoria.Registrar("login.ok", u.Email, usuarioId: u.Id, ator: u.Email);
    }

    public sealed record DoisFatoresPedido(string? Codigo, bool Lembrar, bool LembrarDispositivo);

    [HttpPost("entrar-2fa"), EnableRateLimiting("autenticacao")]
    public async Task<IActionResult> Entrar2fa(DoisFatoresPedido p)
    {
        var u = await entrada.GetTwoFactorAuthenticationUserAsync();
        if (u is null) return Erro("A etapa de login expirou. Digite e-mail e senha de novo.", "expirou", 401);
        var codigo = (p.Codigo ?? "").Replace(" ", "").Replace("-", "");
        var r = await entrada.TwoFactorAuthenticatorSignInAsync(codigo, p.Lembrar, p.LembrarDispositivo);
        if (r.IsLockedOut) return Erro("Muitas tentativas erradas. Espere 15 minutos.", "tentativas", 429);
        if (!r.Succeeded) return Erro("Código incorreto. Confira o horário do celular e use o código atual.", "codigo", 401);
        await AposEntrar(u);
        return Ok(new { ok = true });
    }

    [HttpPost("entrar-recuperacao"), EnableRateLimiting("autenticacao")]
    public async Task<IActionResult> EntrarRecuperacao(DoisFatoresPedido p)
    {
        var u = await entrada.GetTwoFactorAuthenticationUserAsync();
        if (u is null) return Erro("A etapa de login expirou. Digite e-mail e senha de novo.", "expirou", 401);
        var r = await entrada.TwoFactorRecoveryCodeSignInAsync((p.Codigo ?? "").Replace(" ", ""));
        if (!r.Succeeded) return Erro("Código de recuperação inválido ou já usado.", "codigo", 401);
        await auditoria.Registrar("login.codigo-recuperacao", u.Email, usuarioId: u.Id, ator: u.Email);
        await AposEntrar(u);
        return Ok(new { ok = true, aviso = "Você usou um código de recuperação. Cada código só vale uma vez: gere novos em Minha conta." });
    }

    [HttpPost("sair")]
    public async Task<IActionResult> Sair()
    {
        await entrada.SignOutAsync();
        return Ok(new { ok = true });
    }

    // ---------------------------------------------------------------- Senha
    [HttpPost("esqueci-senha"), EnableRateLimiting("autenticacao")]
    public async Task<IActionResult> EsqueciSenha(EmailPedido p)
    {
        var u = Emails.Valido(p.Email) ? await usuarios.FindByEmailAsync(p.Email!) : null;
        if (u is not null && !u.BloqueadoPorAdmin)
        {
            if (!u.EmailConfirmado) await EnviarConfirmacao(u);
            else
            {
                var token = await usuarios.GeneratePasswordResetTokenAsync(u);
                var (a, h) = modelos.RedefinirSenha(u.Nome, $"{UrlPublica}/redefinir-senha.html?id={u.Id}&codigo={Cod(token)}");
                await email.Enviar(u.Email, a, h);
                await auditoria.Registrar("senha.pedido-redefinicao", u.Email, usuarioId: u.Id, ator: u.Email);
            }
        }
        return Ok(new { ok = true, mensagem = "Se existir uma conta com esse e-mail, enviamos um link para criar uma nova senha." });
    }

    public sealed record RedefinirPedido(string? Id, string? Codigo, string? Senha);

    [HttpPost("redefinir-senha"), EnableRateLimiting("autenticacao")]
    public async Task<IActionResult> RedefinirSenha(RedefinirPedido p)
    {
        var u = p.Id is null ? null : await usuarios.FindByIdAsync(p.Id);
        var token = Decod(p.Codigo);
        if (u is null || token is null || u.BloqueadoPorAdmin) return Erro("Link inválido ou vencido. Peça um novo.", "link");
        var r = await usuarios.ResetPasswordAsync(u, token, p.Senha ?? "");
        if (!r.Succeeded) return Erro(r);
        // Quem recebeu o link no e-mail provou que o e-mail é dele; e o bloqueio por tentativas acaba.
        u = (await usuarios.FindByIdAsync(p.Id!))!;
        u.EmailConfirmado = true;
        await usuarios.UpdateAsync(u);
        await usuarios.SetLockoutEndDateAsync(u, null);
        await usuarios.ResetAccessFailedCountAsync(u);
        var (a, h) = modelos.SenhaAlterada(u.Nome);
        await email.Enviar(u.Email, a, h);
        await auditoria.Registrar("senha.redefinida", u.Email, usuarioId: u.Id, ator: u.Email);
        return Ok(new { ok = true, mensagem = "Senha alterada. Entre com a nova senha." });
    }

    public sealed record AlterarSenhaPedido(string? Atual, string? Nova);

    [Authorize, HttpPost("alterar-senha"), EnableRateLimiting("autenticacao")]
    public async Task<IActionResult> AlterarSenha(AlterarSenhaPedido p)
    {
        var u = await Atual();
        if (u is null) return Unauthorized();
        var r = await usuarios.ChangePasswordAsync(u, p.Atual ?? "", p.Nova ?? "");
        if (!r.Succeeded) return Erro(r);
        await entrada.RefreshSignInAsync(u); // esta sessão continua; as outras caem (carimbo mudou)
        var (a, h) = modelos.SenhaAlterada(u.Nome);
        await email.Enviar(u.Email, a, h);
        await auditoria.Registrar("senha.alterada", u.Email);
        return Ok(new { ok = true, mensagem = "Senha alterada. As outras sessões abertas foram encerradas." });
    }

    public sealed record NomePedido(string? Nome);

    [Authorize, HttpPost("alterar-nome")]
    public async Task<IActionResult> AlterarNome(NomePedido p)
    {
        var nome = (p.Nome ?? "").Trim();
        if (nome.Length is < 2 or > 120) return Erro("Informe seu nome (de 2 a 120 letras).");
        var u = await Atual();
        if (u is null) return Unauthorized();
        u.Nome = nome;
        var r = await usuarios.UpdateAsync(u);
        if (!r.Succeeded) return Erro(r);
        await entrada.RefreshSignInAsync(u);
        return Ok(new { ok = true });
    }

    [Authorize, HttpPost("sair-de-todos")]
    public async Task<IActionResult> SairDeTodos()
    {
        var u = await Atual();
        if (u is null) return Unauthorized();
        await usuarios.UpdateSecurityStampAsync(u);
        await entrada.RefreshSignInAsync(u);
        await auditoria.Registrar("sessoes.encerradas", u.Email);
        return Ok(new { ok = true, mensagem = "Pronto: só este aparelho continua conectado." });
    }

    // ---------------------------------------------------------------- Verificação em duas etapas
    [Authorize, HttpPost("dois-fatores/iniciar")]
    public async Task<IActionResult> Iniciar2fa()
    {
        var u = await Atual();
        if (u is null) return Unauthorized();
        if (u.DoisFatores) return Erro("A verificação em duas etapas já está ativa.");
        await usuarios.ResetAuthenticatorKeyAsync(u);
        var chave = (await usuarios.GetAuthenticatorKeyAsync(u))!;
        var emissor = Uri.EscapeDataString(config["Site:Nome"] ?? "Catalogo do HTML");
        var uri = $"otpauth://totp/{emissor}:{Uri.EscapeDataString(u.Email)}?secret={chave}&issuer={emissor}&digits=6";
        var legivel = string.Join(" ", Enumerable.Range(0, (chave.Length + 3) / 4).Select(i => chave.Substring(i * 4, Math.Min(4, chave.Length - i * 4)))).ToLowerInvariant();
        return Ok(new { chave = legivel, uri });
    }

    public sealed record CodigoPedido(string? Codigo, string? Senha);

    [Authorize, HttpPost("dois-fatores/ativar")]
    public async Task<IActionResult> Ativar2fa(CodigoPedido p)
    {
        var u = await Atual();
        if (u is null) return Unauthorized();
        var codigo = (p.Codigo ?? "").Replace(" ", "").Replace("-", "");
        var ok = await usuarios.VerifyTwoFactorTokenAsync(u, usuarios.Options.Tokens.AuthenticatorTokenProvider, codigo);
        if (!ok) return Erro("Código incorreto. Digite o código de 6 números que aparece agora no aplicativo.");
        await usuarios.SetTwoFactorEnabledAsync(u, true);
        var codigos = await usuarios.GenerateNewTwoFactorRecoveryCodesAsync(u, 10);
        await entrada.RefreshSignInAsync(u);
        await auditoria.Registrar("2fa.ativada", u.Email);
        return Ok(new { ok = true, codigosRecuperacao = codigos });
    }

    [Authorize, HttpPost("dois-fatores/novos-codigos")]
    public async Task<IActionResult> NovosCodigos(CodigoPedido p)
    {
        var u = await Atual();
        if (u is null) return Unauthorized();
        if (!u.DoisFatores) return Erro("Ative a verificação em duas etapas primeiro.");
        if (!await usuarios.CheckPasswordAsync(u, p.Senha ?? "")) return Erro("Senha incorreta.");
        var codigos = await usuarios.GenerateNewTwoFactorRecoveryCodesAsync(u, 10);
        await auditoria.Registrar("2fa.novos-codigos", u.Email);
        return Ok(new { ok = true, codigosRecuperacao = codigos });
    }

    [Authorize, HttpPost("dois-fatores/desativar")]
    public async Task<IActionResult> Desativar2fa(CodigoPedido p)
    {
        var u = await Atual();
        if (u is null) return Unauthorized();
        if (!await usuarios.CheckPasswordAsync(u, p.Senha ?? "")) return Erro("Senha incorreta.");
        await usuarios.SetTwoFactorEnabledAsync(u, false);
        await usuarios.ResetAuthenticatorKeyAsync(u);
        await entrada.ForgetTwoFactorClientAsync();
        await entrada.RefreshSignInAsync(u);
        await auditoria.Registrar("2fa.desativada", u.Email);
        return Ok(new { ok = true });
    }

    // ---------------------------------------------------------------- Teste grátis e códigos
    [Authorize, HttpPost("teste-gratis"), EnableRateLimiting("sensivel")]
    public async Task<IActionResult> TesteGratis()
    {
        var u = await Atual();
        if (u is null) return Unauthorized();
        var r = await vendas.IniciarTeste(u);
        return r.ok ? Ok(new { ok = true, mensagem = r.mensagem }) : Erro(r.mensagem);
    }

    [Authorize, HttpPost("resgatar-codigo"), EnableRateLimiting("sensivel")]
    public async Task<IActionResult> ResgatarCodigo(CodigoPedido p)
    {
        var u = await Atual();
        if (u is null) return Unauthorized();
        var r = await vendas.ResgatarCodigo(u, p.Codigo ?? "");
        return r.ok ? Ok(new { ok = true, mensagem = r.mensagem }) : Erro(r.mensagem);
    }

    [Authorize, HttpGet("pedidos")]
    public async Task<IActionResult> MeusPedidos()
    {
        var id = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Ok(await banco.Lista("""
            SELECT p.id, pr.nome, p.plataforma, p.status, p.valor_centavos, p.criado_em
            FROM pedidos p JOIN produtos pr ON pr.id = p.produto_id WHERE p.usuario_id=@u ORDER BY p.criado_em DESC
            """, r => new { id = r.Id("id"), produto = r.Texto("nome"), plataforma = r.Texto("plataforma"), status = r.Texto("status"),
                valor = r.Inteiro("valor_centavos"), criadoEm = r.Data("criado_em") }, ("u", id)));
    }

    // ---------------------------------------------------------------- Progresso
    [Authorize, HttpGet("progresso")]
    public async Task<IActionResult> Progresso()
    {
        var id = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var itens = await banco.Lista("SELECT chave, valor FROM progresso WHERE usuario_id=@u", r => (r.GetString(0), r.GetString(1)), ("u", id));
        return Ok(itens.ToDictionary(i => i.Item1, i => i.Item2));
    }

    public sealed record ProgressoPedido(string? Chave, string? Valor);

    [Authorize, HttpPut("progresso")]
    public async Task<IActionResult> SalvarProgresso(ProgressoPedido p)
    {
        if (string.IsNullOrWhiteSpace(p.Chave) || p.Chave.Length > 120 || (p.Valor?.Length ?? 0) > 2000) return Erro("Dado inválido.");
        var id = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (p.Valor is null)
        {
            await banco.Executar("DELETE FROM progresso WHERE usuario_id=@u AND chave=@c", ("u", id), ("c", p.Chave));
            return Ok(new { ok = true });
        }
        var total = await banco.Escalar<long>("SELECT count(*) FROM progresso WHERE usuario_id=@u", ("u", id));
        if (total >= 1000) return Erro("Limite de itens de progresso atingido.");
        await banco.Executar("""
            INSERT INTO progresso (usuario_id, chave, valor) VALUES (@u, @c, @v)
            ON CONFLICT (usuario_id, chave) DO UPDATE SET valor = excluded.valor, atualizado_em = now()
            """, ("u", id), ("c", p.Chave), ("v", p.Valor));
        return Ok(new { ok = true });
    }

    // ---------------------------------------------------------------- LGPD: meus dados
    [Authorize, HttpGet("exportar-dados")]
    public async Task<IActionResult> ExportarDados()
    {
        var u = await Atual();
        if (u is null) return Unauthorized();
        var dados = new
        {
            geradoEm = DateTimeOffset.UtcNow,
            conta = new { u.Id, u.Nome, u.Email, u.EmailConfirmado, u.CriadoEm, u.UltimoLoginEm, u.TermosVersao, u.TermosAceitosEm, u.DoisFatores },
            papeis = await usuarios.GetRolesAsync(u),
            acessos = await acesso.DoUsuario(u.Id),
            pedidos = await banco.Lista("SELECT p.id, pr.nome, p.plataforma, p.status, p.valor_centavos, p.criado_em FROM pedidos p JOIN produtos pr ON pr.id=p.produto_id WHERE p.usuario_id=@u",
                r => new { id = r.Id("id"), produto = r.Texto("nome"), plataforma = r.Texto("plataforma"), status = r.Texto("status"), valorCentavos = r.Inteiro("valor_centavos"), criadoEm = r.Data("criado_em") }, ("u", u.Id)),
            progresso = await banco.Lista("SELECT chave, valor, atualizado_em FROM progresso WHERE usuario_id=@u",
                r => new { chave = r.Texto("chave"), valor = r.Texto("valor"), atualizadoEm = r.Data("atualizado_em") }, ("u", u.Id)),
            // Avisos das plataformas de venda que citam o seu e-mail (podem ter CPF, telefone e endereço que você informou lá)
            avisosDasPlataformasDeVenda = await banco.Lista(
                "SELECT plataforma, tipo_evento, recebido_em, corpo FROM webhook_eventos WHERE assinatura_valida AND corpo ~* @re ORDER BY recebido_em DESC LIMIT 200",
                r => new { plataforma = r.Texto("plataforma"), evento = r.TextoOuNulo("tipo_evento"), recebidoEm = r.Data("recebido_em"), conteudo = r.Texto("corpo") }, ("re", Emails.PadraoExato(u.Email))),
            registrosDeSeguranca = await banco.Lista("SELECT quando, acao, ip FROM auditoria WHERE usuario_id=@u ORDER BY quando DESC LIMIT 500",
                r => new { quando = r.Data("quando"), acao = r.Texto("acao"), ip = r.TextoOuNulo("ip") }, ("u", u.Id)),
        };
        await auditoria.Registrar("lgpd.dados-exportados", u.Email);
        var json = JsonSerializer.SerializeToUtf8Bytes(dados, new JsonSerializerOptions { WriteIndented = true });
        return File(json, "application/json", "meus-dados.json");
    }

    public sealed record ExcluirPedido(string? Senha, string? Confirmacao);

    [Authorize, HttpPost("excluir"), EnableRateLimiting("autenticacao")]
    public async Task<IActionResult> Excluir(ExcluirPedido p)
    {
        var u = await Atual();
        if (u is null) return Unauthorized();
        if (p.Confirmacao != "EXCLUIR") return Erro("Digite EXCLUIR para confirmar.");
        if (!await usuarios.CheckPasswordAsync(u, p.Senha ?? "")) return Erro("Senha incorreta.");
        if (await usuarios.IsInRoleAsync(u, "admin") &&
            await banco.Escalar<long>("SELECT count(*) FROM usuario_papeis WHERE papel_id='admin'") <= 1)
            return Erro("Você é o único administrador. Passe o papel para outra pessoa antes de excluir a conta.");

        await contas.Anonimizar(u, "a pedido do titular");
        await entrada.SignOutAsync();
        return Ok(new { ok = true, mensagem = "Conta excluída. Guardamos só os registros de venda exigidos por lei, sem ligação com uma conta." });
    }
}
