# Plataforma do Catálogo do HTML — guia do professor

Este é o sistema que vende e protege o curso: contas de aluno, login, tipos de usuário, painel do administrador, planos, vendas (Kiwify, Hotmart, Mercado Pago, Pix direto e escolas) e a liberação automática do conteúdo pago.

Ele segue a sua stack: **C# / ASP.NET Core 10 (Controllers) + PostgreSQL + Docker**, com o HTTPS feito pelo Caddy.

---

## 0. Experimentar no seu computador (antes de contratar servidor)

1. Instale o **Docker Desktop** (Windows ou Mac) e abra-o.
2. No terminal, dentro da pasta `plataforma`:
   ```bash
   docker compose -f docker-compose.local.yml up --build
   ```
   A primeira vez demora alguns minutos (baixa o .NET e o PostgreSQL).
3. Abra **http://localhost:5080**.
4. Crie uma conta com `admin@teste.com`. Os e-mails não são enviados: eles aparecem como arquivos `.html` na pasta `emails-teste`. Abra o arquivo e clique em **Confirmar meu e-mail**.
5. Em Minha conta → Segurança, ative as 2 etapas com o app autenticador do celular. O menu passa a mostrar **Painel**.
6. Crie outras contas com e-mails inventados para testar como aluno. Para registrar uma venda de teste, use Painel → Vendas → Registrar venda direta.

Para parar: `Ctrl + C`. Para apagar tudo e começar do zero: `docker compose -f docker-compose.local.yml down -v`.

---

## 1. O que tem aqui

```
plataforma/
├── site/                 o site inteiro (o mesmo da pasta pagina-completa)
├── src/Plataforma/       o sistema em C#
│   ├── Api/              endpoints: conta, admin, planos, webhooks
│   ├── Acesso/           o "porteiro": decide quem abre cada página
│   ├── Identidade/       usuários, senhas, papéis, permissões, 2 etapas
│   ├── Vendas/           Kiwify, Hotmart, Mercado Pago, cupons, códigos
│   ├── Seguranca/        CSRF, limites de tentativas, cabeçalhos
│   ├── Auditoria/        registro de quem fez o quê
│   ├── Email/            envio de e-mails (fila em segundo plano)
│   └── Dados/Migracoes/  o banco, em SQL (roda sozinho ao iniciar)
├── testes/               testes automáticos da API e das telas
├── Dockerfile, docker-compose.yml, Caddyfile
└── .env.exemplo          as configurações que você preenche
```

A regra mais importante do sistema: **o servidor confere a conta antes de entregar cada página.** Uma página paga nunca chega ao navegador de quem não tem o plano. Esconder com JavaScript não protegeria nada, porque se desliga com F12.

---

## 2. O que fica grátis e o que é pago

A ideia é o aluno provar o curso de verdade antes de pagar.

| Nível | O que é | Onde fica |
|---|---|---|
| **Livre** (sem conta) | Catálogo, Códigos, Dicionário, Verificador, página de IA, mapa dos módulos e dos projetos, Passos 1 a 4 da trilha, **Projeto 1 completo** | Atrai quem chega pelo Google e pelas redes |
| **Conta grátis** | Passos 5 a 10 da trilha, capítulo de HTML, progresso salvo na conta | Quem cria conta vira contato seu (e-mail) |
| **Teste grátis de 7 dias** | Tudo, sem cartão, uma vez por e-mail | Botão na página Minha conta |
| **Pago** | Capítulos de CSS e JS, módulos futuros, Projetos 2 a 12 e os vídeos do resultado esperado | Plano mensal, anual ou pacote de projetos |
| **Equipe** | Painel do administrador | Só papéis de equipe, com 2 etapas |

Você muda qualquer regra no painel, na aba **O que é pago**, sem mexer no código. A primeira regra que combina com o endereço é a que vale. **Página sem regra não é servida**: ao criar uma página nova, crie a regra dela, senão ela dá 404. Assim, nada vaza por esquecimento.

### Planos de exemplo (mude preços e textos no painel)

| Plano | Preço de exemplo | Libera |
|---|---|---|
| Formação mensal | R$ 49,90/mês | Módulos e projetos |
| Formação 12 meses | R$ 397 (de R$ 598,80), em até 12x | Módulos e projetos por 1 ano |
| Pacote de projetos | R$ 197 | Só os projetos, por 1 ano |
| Licença para escolas | Venda direta | Códigos de acesso para a turma |

---

## 3. Tipos de usuário

As permissões de cada papel ficam **no código** (`Identidade/Permissoes.cs`), não no banco. Assim ninguém ganha poderes mexendo só numa tabela. Toda a equipe precisa da verificação em 2 etapas (app autenticador no celular) para entrar no painel.

| Papel | Pode |
|---|---|
| **Aluno** | Estudar o que o plano libera, salvar progresso, baixar e excluir os próprios dados |
| **Suporte** | Ver usuários e pedidos, bloquear e desbloquear alunos, liberar acesso para alunos. Não vê quanto você fatura. |
| **Financeiro** | Ver e registrar vendas, reembolsar, criar cupons e códigos, reprocessar avisos das plataformas |
| **Professor** | Mudar as regras de conteúdo |
| **Administrador** | Tudo, inclusive dar papéis, ver a auditoria e desativar as 2 etapas de alguém que perdeu o celular |

Regras que valem para todos:
- Só um administrador bloqueia ou desbloqueia alguém da equipe.
- Ninguém bloqueia nem desbloqueia a si mesmo.
- Só o administrador libera acesso ou registra venda para a própria conta. Quem gera um código de acesso não pode usá-lo na própria conta.
- Tudo o que a equipe faz fica na **Auditoria** (quem, o quê, quando, de qual IP).

---

## 4. O que deixa o aluno seguro (e o que mostrar na página de vendas)

- **Cartão nunca passa pelo seu site.** O pagamento acontece na Kiwify, Hotmart ou Mercado Pago. A página `compra-segura.html` explica isso ao aluno.
- **Garantia de 7 dias** (direito de arrependimento do Código de Defesa do Consumidor). O reembolso na plataforma retira o acesso sozinho.
- **Senha guardada com hash** (nem você consegue ver). Senhas fracas ou muito comuns são recusadas.
- **Só quem abre o e-mail cria a senha da conta.** O cadastro pede nome e e-mail; a senha é criada no link de confirmação. Ninguém consegue "reservar" o e-mail de outra pessoa.
- **Contra tentativas de adivinhar a senha:** depois de 5 tentativas erradas, a conta trava por 15 minutos. Há limites por IP em todas as rotas de login. A resposta é sempre a mesma, e ninguém descobre quais e-mails têm conta.
- **Verificação em 2 etapas** opcional para alunos e obrigatória para a equipe, com 10 códigos de recuperação.
- **Sessão derrubada:** ao trocar a senha, ser bloqueado ou perder um papel, as sessões abertas caem em até 1 minuto. O aluno pode sair de todos os computadores em Minha conta.
- **Cookies protegidos** (HttpOnly, SameSite, `__Host-`, só em HTTPS), proteção contra CSRF em todos os formulários, CSP e outros cabeçalhos de segurança.
- **LGPD:** Termos e Política de privacidade, botão para baixar todos os dados e botão para excluir a conta. A exclusão apaga nome, e-mail, progresso e o conteúdo dos avisos das plataformas (CPF, telefone e endereço).

---

## 5. Como as vendas liberam o acesso

```
Aluno compra na Kiwify/Hotmart/Mercado Pago
        │  a plataforma avisa o seu sistema (webhook)
        ▼
Sistema confere a assinatura do aviso ──► errada: recusa (e mostra no painel)
        │
        ▼
Guarda o aviso (o mesmo aviso repetido não libera duas vezes)
        │
        ├── o e-mail já tem conta confirmada → libera na hora e manda e-mail
        └── ainda não tem conta → reserva o acesso para o e-mail e manda
            "crie sua conta com este e-mail"; ao ativar, a compra aparece
```

- **Reembolso ou chargeback** retira o acesso, inclusive das renovações da mesma assinatura.
- **Assinatura cancelada:** o aluno continua até o fim do período pago.
- **Aviso com produto não ligado:** fica com erro no painel. Você liga o produto e clica em **Reprocessar**.
- **Venda direta (Pix, dinheiro):** painel → **Vendas** → Registrar venda direta.
- **Escolas:** painel → **Códigos de acesso** → gerar lote (formato `ABCD-EFGH-JKMN`), baixar em CSV e entregar um código por aluno.
- **Cupons:** para o checkout do Mercado Pago dentro do site, crie no painel. Na Kiwify e na Hotmart, crie o cupom no painel delas. No Mercado Pago, o uso do cupom fica reservado quando o aluno abre o pagamento. Se ele não pagar em 24 horas, o link vence e o uso volta. Cada aluno pode ter no máximo 3 pagamentos em aberto.

---

## 6. Colocar no ar

### Do que você precisa

1. **Um servidor VPS com Linux** (Ubuntu 24.04) e pelo menos 2 GB de memória. Provedores comuns: Hostinger, Contabo, DigitalOcean, Hetzner.
2. **Um domínio** (ex.: `catalogodohtml.com.br` no Registro.br), com um registro **A** apontando para o IP do servidor.
3. **Um serviço de e-mail SMTP.** Brevo tem plano grátis de 300 e-mails por dia. Sem SMTP ninguém recebe o link de ativação.
4. **Conta de vendedor** na Kiwify, Hotmart e/ou Mercado Pago. Todas pedem CPF ou CNPJ.

### Passo a passo no servidor

```bash
# 1. Instalar o Docker
curl -fsSL https://get.docker.com | sh

# 2. Mandar esta pasta para o servidor (do seu computador)
scp -r plataforma usuario@IP-DO-SERVIDOR:~/

# 3. No servidor, criar a configuração
cd ~/plataforma
cp .env.exemplo .env
nano .env          # preencha domínio, senha do banco, e-mail do admin, SMTP e as plataformas

# 4. Subir tudo (banco, sistema e HTTPS)
docker compose up -d --build

# 5. Conferir
docker compose ps
curl https://SEU-DOMINIO/api/saude
```

O Caddy pede o certificado HTTPS sozinho na primeira vez. Para isso, o domínio já precisa apontar para o servidor e as portas 80 e 443 precisam estar abertas.

### Primeiro acesso de administrador

1. No `.env`, coloque o seu e-mail em `Plataforma__AdminInicial`.
2. No site, crie a conta com esse e-mail e ative pelo link.
3. Em **Minha conta → Segurança**, ative as 2 etapas (Google Authenticator, Microsoft Authenticator ou Aegis). **Guarde os 10 códigos de recuperação num lugar fora do celular.**
4. Pronto: o menu mostra **Painel**.

---

## 7. Ligar as plataformas de venda

O endereço que você cadastra em cada plataforma é:

| Plataforma | Endereço do webhook | O que copiar para o `.env` |
|---|---|---|
| Kiwify | `https://SEU-DOMINIO/api/webhooks/kiwify` | O token do webhook → `Vendas__Kiwify__Token` |
| Hotmart | `https://SEU-DOMINIO/api/webhooks/hotmart` | O Hottok → `Vendas__Hotmart__Hottok` |
| Mercado Pago | `https://SEU-DOMINIO/api/webhooks/mercadopago` | Access token → `Vendas__MercadoPago__AccessToken`; assinatura secreta → `Vendas__MercadoPago__SegredoWebhook` |

Depois, **no painel → Produtos e preços**:
- Em cada produto, cole o **link de checkout** da Kiwify/Hotmart. É para onde o botão Comprar leva.
- Em **Ids nas plataformas**, ligue o id do produto na plataforma ao produto do site. Sem isso o sistema não sabe o que liberar.

Depois de mudar o `.env`: `docker compose up -d`.

> **Antes da primeira venda real, faça uma compra de teste em cada plataforma.** Use o botão de teste de webhook e, se der, uma compra de R$ 1 com reembolso. Veja no painel, em **Avisos das plataformas**, se chegou com "assinatura válida" e liberou o acesso.
>
> Os campos de cada plataforma foram escritos a partir da documentação delas, que muda de tempos em tempos. Os testes automáticos confirmam a lógica, mas não um aviso real da Kiwify ou da Hotmart. Se algum campo vier diferente, o aviso aparece com erro no painel e nada é liberado por engano: me mande o conteúdo do aviso que eu ajusto.

**Assinatura mensal:** use Kiwify ou Hotmart, que cobram todo mês sozinhas. O checkout do Mercado Pago dentro do site vende só planos de pagamento único (anual, pacote).

---

## 8. Rotina

### Backup do banco (todo dia)

Coloque no `crontab -e` do servidor:

```bash
0 3 * * * cd ~/plataforma && docker compose exec -T banco pg_dump -U plataforma plataforma | gzip > ~/backups/banco-$(date +\%F).sql.gz && find ~/backups -mtime +30 -delete
```

Copie a pasta `~/backups` para fora do servidor de vez em quando (Google Drive, HD externo). Backup que só existe no mesmo servidor some junto com ele.

### Atualizar o site ou o sistema

```bash
# do seu computador: manda a pasta nova
scp -r plataforma usuario@IP:~/
# no servidor
cd ~/plataforma && docker compose up -d --build
```

As mudanças no banco (arquivos novos em `Dados/Migracoes/`) rodam sozinhas ao iniciar. **Nunca edite uma migração que já rodou:** crie a próxima (`005_...sql`).

### Colocar o vídeo do resultado esperado de um projeto

1. Grave o vídeo e salve como `site/midia-projetos/projeto-2.mp4`. Capturas vão como `projeto-2-01-celular.webp`.
2. Rode `python ferramentas/gerar-projetos.py` (Python 3.12 ou mais novo).
3. Atualize o servidor.

Os arquivos do Projeto 1 são livres; os dos outros projetos só abrem para quem tem plano.

### Ver o que está acontecendo

```bash
docker compose logs -f sistema      # o que o sistema está fazendo
docker compose ps                   # se está tudo de pé
```

---

## 9. Rodar os testes (no seu computador)

Com o sistema rodando em modo de desenvolvimento, apontando para um banco **novo**:

```bash
pip install requests playwright
python testes/testar_api.py --url http://localhost:5080 --emails caminho/da/pasta/emails-teste
python testes/testar_telas.py --emails caminho/da/pasta/emails-teste --fotos capturas
```

O `testar_api.py` confere 97 situações, incluindo ataques:
- conteúdo pago sem plano;
- aluno tentando entrar no painel;
- suporte tentando virar admin;
- aviso de venda falso;
- link de confirmação reusado;
- senha de quem "reservou" o e-mail de outra pessoa;
- teste grátis repetido com `+apelido`.

Todos passaram na última versão.

---

## 10. Antes de vender: checklist

- [ ] `.env` preenchido. `BANCO_SENHA` longa e aleatória. O `.env` **nunca** vai para o GitHub (já está no `.gitignore`).
- [ ] **Repositório privado.** O repositório público `catalogo-html` hoje tem as páginas que agora são pagas. Deixe-o privado, ou mantenha nele só a parte grátis. Lembre que o que já foi público pode ter sido copiado.
- [ ] Termos de uso e Política de privacidade: troque `contato@seudominio.com.br` e os campos entre colchetes, **apague a caixa "Para o professor"** e peça para um advogado ou contador revisar.
- [ ] SMTP funcionando: crie uma conta de teste e veja o e-mail chegar (e não cair no spam).
- [ ] Compra de teste em cada plataforma, com reembolso.
- [ ] 2 etapas ativas na sua conta e códigos de recuperação guardados.
- [ ] Backup diário configurado e testado uma vez: restaure num banco de teste.
- [ ] Situação legal: você é servidor público. Confirme com o RH ou o jurídico da Secretaria de Educação se pode vender como pessoa física ou abrir empresa (MEI ou outra) antes de emitir a primeira nota.

---

## 11. Limites que você precisa saber

- **O navegador do aluno sempre recebe a página que ele pode ver.** Quem pagou consegue salvar a página ou gravar a tela; nenhum sistema impede isso. O que o sistema garante é que ninguém **sem** plano recebe o conteúdo.
- **Travamento por tentativas:** alguém que sabe o e-mail de um aluno pode errar a senha 5 vezes de propósito e travar a entrada dele por 15 minutos. É o preço de proteger contra quem tenta adivinhar senhas. A pessoa pode destravar na hora pelo "Esqueci minha senha".
- **Um servidor só:** se o VPS cair, o site cai. Para o começo é o suficiente. Quando as vendas crescerem, o próximo passo é um banco gerenciado e um segundo servidor.
