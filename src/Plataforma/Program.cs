// =====================================================================
// PLATAFORMA DO CATÁLOGO DO HTML — ponto de partida do sistema
// Monta os serviços (banco, login, permissões, vendas) e a ordem em que
// cada pedido do navegador passa por eles (o "pipeline").
// =====================================================================
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Npgsql;
using Plataforma.Acesso;
using Plataforma.Api;
using Plataforma.Auditoria;
using Plataforma.Dados;
using Plataforma.Email;
using Plataforma.Identidade;
using Plataforma.Seguranca;
using Plataforma.Vendas;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
var producao = builder.Environment.IsProduction();

builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 1024 * 1024);

// ---------- Banco ----------
var conexao = config.GetConnectionString("Banco") ?? throw new InvalidOperationException("Configure ConnectionStrings:Banco.");
builder.Services.AddSingleton(NpgsqlDataSource.Create(conexao));
builder.Services.AddSingleton<Banco>();

// ---------- Login (ASP.NET Core Identity com as tabelas próprias) ----------
builder.Services.AddIdentity<Usuario, Papel>(o =>
    {
        o.Password.RequiredLength = 10;          // senha longa vale mais que "símbolo obrigatório"
        o.Password.RequiredUniqueChars = 4;
        o.Password.RequireDigit = false;
        o.Password.RequireLowercase = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireNonAlphanumeric = false;
        o.Lockout.MaxFailedAccessAttempts = 5;   // 5 erros seguidos = 15 minutos de espera
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        o.Lockout.AllowedForNewUsers = true;
        o.User.RequireUniqueEmail = true;
        o.User.AllowedUserNameCharacters = "";   // o "usuário" é o e-mail; qualquer caractere de e-mail vale
        o.SignIn.RequireConfirmedEmail = false;  // conferido à mão no login, DEPOIS da senha (não revela contas)
        o.Tokens.EmailConfirmationTokenProvider = "ConfirmacaoEmail";
    })
    .AddUserStore<UsuarioStore>()
    .AddRoleStore<PapelStore>()
    .AddErrorDescriber<ErrosPt>()
    .AddPasswordValidator<ValidadorSenhaComum>()
    .AddClaimsPrincipalFactory<FabricaCracha>()
    .AddDefaultTokenProviders()
    .AddTokenProvider<ProvedorConfirmacaoEmail>("ConfirmacaoEmail");

builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(2)); // redefinir senha
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1)); // bloqueio vale em até 1 min

var sufixoCookie = producao ? "__Host-" : "";
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = sufixoCookie + "sessao";
    o.Cookie.HttpOnly = true;                    // JavaScript não lê o cookie (protege contra roubo por XSS)
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = producao ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromDays(14);
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = ctx => Responder(ctx, 401, "/entrar.html?volta=" + Uri.EscapeDataString(ctx.Request.Path));
    o.Events.OnRedirectToAccessDenied = ctx => Responder(ctx, 403, "/");
});
foreach (var esquema in new[] { IdentityConstants.TwoFactorUserIdScheme, IdentityConstants.TwoFactorRememberMeScheme })
    builder.Services.Configure<CookieAuthenticationOptions>(esquema, o =>
    {
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = producao ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        o.Cookie.SameSite = SameSiteMode.Lax;
    });

static Task Responder(RedirectContext<CookieAuthenticationOptions> ctx, int status, string destino)
{
    if (ctx.Request.Path.StartsWithSegments("/api")) ctx.Response.StatusCode = status;
    else ctx.Response.Redirect(destino);
    return Task.CompletedTask;
}

builder.Services.AddDataProtection()
    .SetApplicationName("plataforma-catalogo")
    .PersistKeysToFileSystem(new DirectoryInfo(config["DataProtection:Pasta"] ?? "chaves"));

builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF";
    o.Cookie.Name = sufixoCookie + "csrf";
    o.Cookie.SecurePolicy = producao ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
});

// ---------- Permissões ----------
builder.Services.AddAuthorization(Permissoes.Registrar);
builder.Services.AddScoped<IAuthorizationHandler, VerificadorPermissao>();

// ---------- Limite de tentativas por IP (contra robôs e força bruta) ----------
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        await ctx.HttpContext.Response.WriteAsync("""{"erro":"Muitas tentativas em pouco tempo. Espere um minuto e tente de novo."}""", ct);
    };
    static string Ip(HttpContext c) => c.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
    RateLimitPartition<string> Janela(HttpContext c, int limite) =>
        RateLimitPartition.GetFixedWindowLimiter(Ip(c), _ => new FixedWindowRateLimiterOptions { PermitLimit = limite, Window = TimeSpan.FromMinutes(1) });
    o.AddPolicy("autenticacao", c => Janela(c, config.GetValue("Seguranca:LimiteLoginPorMinuto", 10)));
    o.AddPolicy("sensivel", c => Janela(c, 10));
    o.AddPolicy("webhook", c => Janela(c, 300));
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(c => Janela(c, config.GetValue("Seguranca:LimiteGeralPorMinuto", 600)));
});

// ---------- Serviços do sistema ----------
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddScoped<RegistroAuditoria>();
builder.Services.AddScoped<ServicoAcesso>();
builder.Services.AddScoped<ServicoContas>();
builder.Services.AddScoped<ServicoVendas>();
builder.Services.AddScoped<ProcessadorWebhooks>();
builder.Services.AddSingleton<RegrasConteudo>();
builder.Services.AddSingleton<ModelosEmail>();
builder.Services.AddScoped<Kiwify>();
builder.Services.AddScoped<Hotmart>();
builder.Services.AddScoped<MercadoPago>();
builder.Services.AddScoped<IPlataformaVenda>(sp => sp.GetRequiredService<Kiwify>());
builder.Services.AddScoped<IPlataformaVenda>(sp => sp.GetRequiredService<Hotmart>());
builder.Services.AddScoped<IPlataformaVenda>(sp => sp.GetRequiredService<MercadoPago>());
// O envio de verdade (SMTP, ou arquivo em desenvolvimento) fica atrás de uma fila em segundo plano.
if (string.IsNullOrWhiteSpace(config["Email:Smtp:Servidor"])) builder.Services.AddSingleton<EnviadorArquivo>();
else builder.Services.AddSingleton<EnviadorSmtp>();
builder.Services.AddSingleton(sp => new FilaEmail(
    string.IsNullOrWhiteSpace(config["Email:Smtp:Servidor"]) ? sp.GetRequiredService<EnviadorArquivo>() : sp.GetRequiredService<EnviadorSmtp>(),
    sp.GetRequiredService<ILogger<FilaEmail>>()));
builder.Services.AddSingleton<IEnviadorEmail>(sp => sp.GetRequiredService<FilaEmail>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<FilaEmail>());
builder.Services.AddHostedService<ExpirarPedidos>();

builder.Services.AddControllers();
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    o.KnownIPNetworks.Clear();   // o proxy (Caddy) é o único que fala com o sistema: a porta não fica aberta na internet
    o.KnownProxies.Clear();
});
builder.Services.AddHsts(o => { o.MaxAge = TimeSpan.FromDays(365); o.IncludeSubDomains = true; });

var app = builder.Build();

// ---------- Migrações do banco ----------
await Migrador.Aplicar(app.Services.GetRequiredService<NpgsqlDataSource>(), app.Logger);
if (producao && string.IsNullOrWhiteSpace(config["Email:Smtp:Servidor"]))
    app.Logger.LogCritical("PRODUÇÃO SEM SMTP: e-mails de confirmação e senha NÃO estão sendo enviados. Configure Email:Smtp.");
if (producao && string.IsNullOrWhiteSpace(config["Site:UrlPublica"]))
    throw new InvalidOperationException("Configure Site:UrlPublica (ex.: https://seusite.com.br). Os links dos e-mails usam esse endereço.");

// ---------- Pipeline: a ordem importa ----------
if (config.GetValue("Rede:AtrasDeProxy", false)) app.UseForwardedHeaders();
app.UseMiddleware<CabecalhosSeguranca>();
if (producao) app.UseHsts();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// O porteiro confere a regra do caminho ANTES de entregar o arquivo
app.UseMiddleware<PortaoDoSite>();
var pastaSite = Path.GetFullPath(config["Site:Pasta"] ?? "site");
var tipos = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
tipos.Mappings[".vtt"] = "text/vtt";                 // legendas dos vídeos
tipos.Mappings[".webmanifest"] = "application/manifest+json";
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(pastaSite),
    ContentTypeProvider = tipos,
    OnPrepareResponse = c => PortaoDoSite.AjustarCabecalhos(c.Context),
});

app.MapControllers();
app.MapGet("/api/saude", async (Banco b) => Results.Ok(new { ok = await b.Escalar<int>("SELECT 1") == 1, hora = DateTimeOffset.UtcNow }))
   .DisableRateLimiting();
app.MapFallback(ctx =>
{
    ctx.Response.StatusCode = 404;
    return ctx.Request.Path.StartsWithSegments("/api")
        ? ctx.Response.WriteAsJsonAsync(new { erro = "Endereço não encontrado." })
        : ctx.Response.WriteAsync("Página não encontrada.");
});

app.Logger.LogInformation("Site servido da pasta {Pasta}", pastaSite);
app.Run();

/// <summary>Link de confirmação de e-mail: vale 24 horas (o de senha vale 2).</summary>
public sealed class ProvedorConfirmacaoEmail(IDataProtectionProvider dp, IOptions<OpcoesConfirmacaoEmail> o, ILogger<DataProtectorTokenProvider<Usuario>> log)
    : DataProtectorTokenProvider<Usuario>(dp, o, log);

public sealed class OpcoesConfirmacaoEmail : DataProtectionTokenProviderOptions
{
    public OpcoesConfirmacaoEmail() { Name = "ConfirmacaoEmail"; TokenLifespan = TimeSpan.FromHours(24); }
}

public partial class Program;
