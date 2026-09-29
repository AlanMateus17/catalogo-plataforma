using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;

namespace Plataforma.Email;

public interface IEnviadorEmail
{
    Task Enviar(string para, string assunto, string html);
}

/// <summary>Envia por SMTP (Brevo, Amazon SES, Zoho, Gmail com senha de app...). Configure Email:Smtp:*.</summary>
public sealed class EnviadorSmtp(IConfiguration config, ILogger<EnviadorSmtp> log) : IEnviadorEmail
{
    public async Task Enviar(string para, string assunto, string html)
    {
        var s = config.GetSection("Email:Smtp");
        using var cliente = new SmtpClient(s["Servidor"], s.GetValue("Porta", 587))
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(s["Usuario"], s["Senha"]),
        };
        using var msg = new MailMessage(new MailAddress(config["Email:Remetente"]!, config["Email:NomeRemetente"] ?? "Catálogo do HTML"),
            new MailAddress(para)) { Subject = assunto, Body = html, IsBodyHtml = true };
        await cliente.SendMailAsync(msg);
        log.LogInformation("E-mail enviado para {Para}: {Assunto}", para, assunto);
    }
}

/// <summary>
/// Para desenvolvimento: em vez de enviar, grava o e-mail como arquivo .html
/// na pasta Email:PastaTeste (abra no navegador para clicar nos links).
/// </summary>
public sealed class EnviadorArquivo(IConfiguration config, ILogger<EnviadorArquivo> log) : IEnviadorEmail
{
    public async Task Enviar(string para, string assunto, string html)
    {
        var pasta = config["Email:PastaTeste"] ?? "emails-teste";
        Directory.CreateDirectory(pasta);
        var nome = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{string.Concat(para.Where(char.IsLetterOrDigit))}.html";
        await File.WriteAllTextAsync(Path.Combine(pasta, nome), $"<!-- para: {para} -->\n<h2>{HtmlEncoder.Default.Encode(assunto)}</h2>\n{html}");
        log.LogWarning("E-mail NÃO enviado (modo teste). Gravado em {Arquivo}", Path.Combine(pasta, nome));
    }
}

/// <summary>Textos dos e-mails. Todo dado do usuário passa por HtmlEncoder.</summary>
public sealed class ModelosEmail(IConfiguration config)
{
    static readonly HtmlEncoder H = HtmlEncoder.Default;
    string Base => (config["Site:UrlPublica"] ?? "http://localhost:5080").TrimEnd('/');

    static string Moldura(string titulo, string corpo) => $"""
        <div style="font-family:Arial,sans-serif;max-width:560px;margin:auto;color:#1c1917;line-height:1.5">
          <h1 style="font-size:22px">{titulo}</h1>
          {corpo}
          <hr style="border:none;border-top:1px solid #ddd;margin:24px 0">
          <p style="font-size:13px;color:#57534e">Você recebeu este e-mail porque ele foi usado no Catálogo do HTML.
          Se não foi você, ignore esta mensagem: nada muda na sua conta.</p>
        </div>
        """;

    static string Botao(string url, string texto) =>
        $"<p><a href=\"{H.Encode(url)}\" style=\"display:inline-block;background:#b45309;color:#fff;padding:12px 20px;border-radius:8px;text-decoration:none;font-weight:bold\">{texto}</a></p>" +
        $"<p style=\"font-size:13px\">Se o botão não funcionar, copie este endereço no navegador:<br>{H.Encode(url)}</p>";

    public (string, string) Confirmacao(string nome, string url) => ("Confirme seu e-mail", Moldura($"Olá, {H.Encode(nome)}!",
        $"<p>Falta um passo para ativar sua conta: confirme que este e-mail é seu.</p>{Botao(url, "Confirmar meu e-mail")}<p>O link vale por 24 horas.</p>"));

    public (string, string) JaTemConta(string nome) => ("Você já tem conta", Moldura($"Olá, {H.Encode(nome)}!",
        $"<p>Alguém tentou criar uma conta com este e-mail, mas ele já está cadastrado.</p>{Botao(Base + "/entrar.html", "Entrar")}<p>Esqueceu a senha? Use \"Esqueci minha senha\" na tela de entrada.</p>"));

    public (string, string) RedefinirSenha(string nome, string url) => ("Redefinir sua senha", Moldura($"Olá, {H.Encode(nome)}!",
        $"<p>Recebemos um pedido para trocar a senha da sua conta.</p>{Botao(url, "Criar nova senha")}<p>O link vale por 2 horas e só funciona uma vez. Se não foi você, ignore: sua senha continua a mesma.</p>"));

    public (string, string) SenhaAlterada(string nome) => ("Sua senha foi alterada", Moldura($"Olá, {H.Encode(nome)}!",
        $"<p>A senha da sua conta acabou de ser alterada, e as outras sessões abertas foram encerradas.</p><p><strong>Não foi você?</strong> Use \"Esqueci minha senha\" agora e responda este e-mail.</p>"));

    public (string, string) AcessoLiberado(string nome, string produto, DateTimeOffset? fim) => ($"Acesso liberado: {produto}", Moldura($"Tudo certo, {H.Encode(nome)}!",
        $"<p>Seu acesso a <strong>{H.Encode(produto)}</strong> está liberado{(fim is null ? "" : $" até {fim.Value.ToOffset(TimeSpan.FromHours(-3)):dd/MM/yyyy}")}.</p>{Botao(Base + "/conta.html", "Ir para minha conta")}"));

    public (string, string) CompraSemConta(string produto, string email) => ($"Sua compra: {produto}", Moldura("Pagamento aprovado!",
        $"<p>Seu acesso a <strong>{H.Encode(produto)}</strong> está reservado para o e-mail <strong>{H.Encode(email)}</strong>.</p><p>Crie sua conta com <strong>este mesmo e-mail</strong> para liberar:</p>{Botao(Base + "/cadastro.html?email=" + Uri.EscapeDataString(email), "Criar minha conta")}"));
}

/// <summary>
/// Fila de e-mails: a requisição só coloca o e-mail na fila e responde na hora.
/// Assim o tempo de resposta de "esqueci a senha" ou "cadastro" não revela se o e-mail existe,
/// e uma lentidão do SMTP não trava o site. Um serviço em segundo plano envia (com 3 tentativas).
/// </summary>
public sealed class FilaEmail : BackgroundService, IEnviadorEmail
{
    readonly System.Threading.Channels.Channel<(string para, string assunto, string html)> _fila =
        System.Threading.Channels.Channel.CreateBounded<(string, string, string)>(new System.Threading.Channels.BoundedChannelOptions(50000)
            { FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest });
    readonly IEnviadorEmail _real;
    readonly ILogger<FilaEmail> _log;

    public FilaEmail(IEnviadorEmail real, ILogger<FilaEmail> log) { _real = real; _log = log; }

    public Task Enviar(string para, string assunto, string html)
    {
        _fila.Writer.TryWrite((para, assunto, html));
        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken parar)
    {
        await foreach (var (para, assunto, html) in _fila.Reader.ReadAllAsync(parar))
        {
            for (var tentativa = 1; tentativa <= 3; tentativa++)
            {
                try { await _real.Enviar(para, assunto, html); break; }
                catch (Exception ex) when (tentativa < 3)
                {
                    _log.LogWarning(ex, "Falha ao enviar e-mail (tentativa {N}); tentando de novo", tentativa);
                    await Task.Delay(TimeSpan.FromSeconds(5 * tentativa), parar);
                }
                catch (Exception ex) { _log.LogError(ex, "E-mail para {Para} não foi enviado: {Assunto}", para, assunto); }
            }
        }
    }
}
