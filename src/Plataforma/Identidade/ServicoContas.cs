using Microsoft.AspNetCore.Identity;
using Plataforma.Auditoria;
using Plataforma.Dados;

namespace Plataforma.Identidade;

/// <summary>Operações de conta usadas pelo aluno e pelo painel.</summary>
public sealed class ServicoContas(UserManager<Usuario> usuarios, Banco banco, IConfiguration config, RegistroAuditoria auditoria)
{
    /// <summary>
    /// O primeiro administrador: o e-mail em Plataforma:AdminInicial vira admin
    /// quando essa conta confirma o e-mail ou entra. Depois, os papéis são dados pelo painel.
    /// </summary>
    public async Task GarantirAdminInicial(Usuario u)
    {
        var inicial = config["Plataforma:AdminInicial"];
        if (string.IsNullOrWhiteSpace(inicial) || !u.EmailConfirmado) return;
        if (Emails.Normalizar(inicial) != u.EmailNormalizado) return;
        if (await usuarios.IsInRoleAsync(u, "admin")) return;
        await usuarios.AddToRoleAsync(u, "admin");
        await auditoria.Registrar("papel.admin-inicial", u.Email, usuarioId: u.Id, ator: "sistema");
    }

    public async Task Bloquear(Usuario u, string motivo)
    {
        u.BloqueadoPorAdmin = true;
        u.MotivoBloqueio = motivo;
        u.BloqueioAtivo = true;
        u.BloqueioAte = DateTimeOffset.MaxValue;
        await usuarios.UpdateAsync(u);
        await usuarios.UpdateSecurityStampAsync(u); // derruba as sessões abertas
    }

    public async Task Desbloquear(Usuario u)
    {
        u.BloqueadoPorAdmin = false;
        u.MotivoBloqueio = null;
        u.BloqueioAte = null;
        u.FalhasAcesso = 0;
        await usuarios.UpdateAsync(u);
    }

    /// <summary>
    /// "Excluir conta" (LGPD): apaga os dados pessoais e o progresso. Os pedidos continuam
    /// (obrigação fiscal), mas sem ligação com a conta.
    /// </summary>
    public async Task Anonimizar(Usuario u, string motivo)
    {
        var emailOriginal = u.Email;
        var emailNormOriginal = u.EmailNormalizado;
        var anonimo = $"excluido-{u.Id:N}@conta.invalid";
        await banco.Executar("DELETE FROM progresso WHERE usuario_id=@u", ("u", u.Id));
        // Pedidos ficam (registro da venda), mas sem nome e e-mail: nem voltam a se ligar a uma conta nova com o mesmo e-mail.
        await banco.Executar("""
            UPDATE pedidos SET email_comprador=@a, email_normalizado=upper(@a), nome_comprador=NULL
            WHERE usuario_id=@u OR email_normalizado=@en
            """, ("u", u.Id), ("a", anonimo), ("en", emailNormOriginal));
        await banco.Executar("UPDATE acessos SET email_normalizado=upper(@a) WHERE email_normalizado=@en", ("a", anonimo), ("en", emailNormOriginal));
        // Avisos das plataformas de venda guardam CPF, telefone e endereço: apaga o conteúdo dos que citam o e-mail.
        await banco.Executar("""
            UPDATE webhook_eventos SET corpo='{"removido":"conta excluída (LGPD)"}', consulta=NULL
            WHERE processado AND corpo ~* @re
            """, ("re", Emails.PadraoExato(emailOriginal)));
        // Registros de auditoria feitos por outras pessoas que citam o e-mail
        await banco.Executar("UPDATE auditoria SET alvo=NULL WHERE lower(alvo)=lower(@e)", ("e", emailOriginal));
        await banco.Executar("UPDATE auditoria SET detalhes=NULL WHERE detalhes ~* @re", ("re", Emails.PadraoExato(emailOriginal)));
        await banco.Executar("UPDATE auditoria SET ator='conta excluída' WHERE lower(ator)=lower(@e)", ("e", emailOriginal));
        await banco.Executar("DELETE FROM usuario_papeis WHERE usuario_id=@u", ("u", u.Id));
        await banco.Executar("UPDATE pedidos SET usuario_id=NULL WHERE usuario_id=@u", ("u", u.Id));
        await banco.Executar("UPDATE acessos SET status='revogado', motivo='conta excluída' WHERE usuario_id=@u AND status='ativo'", ("u", u.Id));
        await banco.Executar("""
            UPDATE usuarios SET nome='Conta excluída', email=@e, email_normalizado=upper(@e), email_confirmado=false,
                senha_hash=NULL, chave_autenticador=NULL, codigos_recuperacao=NULL, dois_fatores=false,
                carimbo_seguranca=@s, excluido_em=now(), bloqueado_por_admin=true, bloqueio_ate='infinity'
            WHERE id=@u
            """, ("u", u.Id), ("e", $"excluido-{u.Id:N}@conta.invalid"), ("s", Guid.NewGuid().ToString("N")));
        await banco.Executar("UPDATE acessos SET email_normalizado=upper(@e) WHERE usuario_id=@u", ("u", u.Id), ("e", $"excluido-{u.Id:N}@conta.invalid"));
        await banco.Executar("UPDATE auditoria SET ator='conta excluída', alvo=NULL WHERE usuario_id=@u", ("u", u.Id));
        await auditoria.Registrar("conta.excluida", u.Id.ToString(), new { motivo }, usuarioId: u.Id, ator: "conta excluída");
    }
}
