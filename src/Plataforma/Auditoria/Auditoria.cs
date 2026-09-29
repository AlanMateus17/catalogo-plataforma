using System.Security.Claims;
using System.Text.Json;
using Plataforma.Dados;

namespace Plataforma.Auditoria;

/// <summary>
/// Registra quem fez o quê: logins, mudanças de papel, acessos concedidos, vendas...
/// Nada aqui é apagado pelas telas: é o histórico que responde "quem liberou isso?".
/// </summary>
public sealed class RegistroAuditoria(Banco banco, IHttpContextAccessor http)
{
    public Task Registrar(string acao, string? alvo = null, object? detalhes = null, Guid? usuarioId = null, string? ator = null)
    {
        var ctx = http.HttpContext;
        var u = ctx?.User;
        var id = usuarioId ?? (Guid.TryParse(u?.FindFirstValue(ClaimTypes.NameIdentifier), out var g) ? g : null);
        var quem = ator ?? u?.FindFirstValue(ClaimTypes.Email) ?? u?.FindFirstValue(ClaimTypes.Name) ?? "sistema";
        var ip = ctx?.Connection.RemoteIpAddress?.ToString();
        var texto = detalhes is null ? null : detalhes as string ?? JsonSerializer.Serialize(detalhes);
        return banco.Executar(
            "INSERT INTO auditoria (usuario_id, ator, acao, alvo, detalhes, ip) VALUES (@u, @a, @acao, @alvo, @d, @ip)",
            ("u", id), ("a", quem), ("acao", acao), ("alvo", alvo), ("d", texto), ("ip", ip));
    }
}
