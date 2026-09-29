namespace Plataforma.Identidade;

/// <summary>Uma conta do site (aluno ou equipe). Espelha a tabela usuarios.</summary>
public sealed class Usuario
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Nome { get; set; } = "";
    public string Email { get; set; } = "";
    public string EmailNormalizado { get; set; } = "";
    public bool EmailConfirmado { get; set; }
    public string? SenhaHash { get; set; }
    public string CarimboSeguranca { get; set; } = Guid.NewGuid().ToString("N");
    public string CarimboConcorrencia { get; set; } = Guid.NewGuid().ToString("N");
    public bool BloqueioAtivo { get; set; } = true;
    public DateTimeOffset? BloqueioAte { get; set; }
    public int FalhasAcesso { get; set; }
    public bool DoisFatores { get; set; }
    public string? ChaveAutenticador { get; set; }
    public string? CodigosRecuperacao { get; set; }
    public bool BloqueadoPorAdmin { get; set; }
    public string? MotivoBloqueio { get; set; }
    public string? TermosVersao { get; set; }
    public DateTimeOffset? TermosAceitosEm { get; set; }
    public DateTimeOffset? TesteUsadoEm { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UltimoLoginEm { get; set; }
    public DateTimeOffset? ExcluidoEm { get; set; }
}

public sealed class Papel
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
}
