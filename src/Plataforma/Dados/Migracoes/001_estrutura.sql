-- =====================================================================
-- 001 — ESTRUTURA DO BANCO
-- Roda uma vez só (a tabela schema_migracoes guarda o que já rodou).
-- Para mudar o banco depois, crie 00X_nome.sql; nunca edite este arquivo.
-- =====================================================================

-- ---------- Usuários e papéis ----------
CREATE TABLE usuarios (
    id                    uuid PRIMARY KEY,
    nome                  text        NOT NULL CHECK (length(nome) BETWEEN 2 AND 120),
    email                 text        NOT NULL,
    email_normalizado     text        NOT NULL UNIQUE,
    email_confirmado      boolean     NOT NULL DEFAULT false,
    senha_hash            text,
    carimbo_seguranca     text        NOT NULL,           -- muda quando senha/papéis mudam: derruba sessões antigas
    carimbo_concorrencia  text        NOT NULL,
    bloqueio_ativo        boolean     NOT NULL DEFAULT true,
    bloqueio_ate          timestamptz,                    -- bloqueio por tentativas erradas (ou manual)
    falhas_acesso         integer     NOT NULL DEFAULT 0,
    dois_fatores          boolean     NOT NULL DEFAULT false,
    chave_autenticador    text,
    codigos_recuperacao   text,
    bloqueado_por_admin   boolean     NOT NULL DEFAULT false,
    motivo_bloqueio       text,
    termos_versao         text,
    termos_aceitos_em     timestamptz,
    teste_usado_em        timestamptz,                    -- teste grátis: uma vez por conta
    criado_em             timestamptz NOT NULL DEFAULT now(),
    ultimo_login_em       timestamptz,
    excluido_em           timestamptz                     -- conta excluída a pedido (dados anonimizados)
);

CREATE TABLE papeis (
    id    text PRIMARY KEY,         -- aluno, suporte, financeiro, professor, admin
    nome  text NOT NULL
);

CREATE TABLE usuario_papeis (
    usuario_id uuid NOT NULL REFERENCES usuarios(id) ON DELETE CASCADE,
    papel_id   text NOT NULL REFERENCES papeis(id),
    PRIMARY KEY (usuario_id, papel_id)
);

-- ---------- Produtos (o que se vende) ----------
CREATE TABLE produtos (
    id                  serial PRIMARY KEY,
    slug                text    NOT NULL UNIQUE CHECK (slug ~ '^[a-z0-9-]+$'),
    nome                text    NOT NULL,
    descricao           text    NOT NULL DEFAULT '',
    tipo                text    NOT NULL CHECK (tipo IN ('assinatura','unico','licenca','teste')),
    preco_centavos      integer NOT NULL CHECK (preco_centavos >= 0),
    preco_de_centavos   integer CHECK (preco_de_centavos >= 0),     -- "de R$ X por R$ Y" (opcional)
    parcelas            integer NOT NULL DEFAULT 1 CHECK (parcelas BETWEEN 1 AND 12),
    dias_acesso         integer CHECK (dias_acesso > 0),            -- NULL = vitalício
    liberacoes          text[]  NOT NULL DEFAULT '{}',              -- ex.: {modulos,projetos}
    beneficios          text[]  NOT NULL DEFAULT '{}',              -- lista mostrada na página de planos
    url_kiwify          text,
    url_hotmart         text,
    mercado_pago        boolean NOT NULL DEFAULT false,             -- vender pelo checkout do Mercado Pago do próprio site
    ativo               boolean NOT NULL DEFAULT true,              -- aparece na página de planos
    destaque            boolean NOT NULL DEFAULT false,
    ordem               integer NOT NULL DEFAULT 0,
    criado_em           timestamptz NOT NULL DEFAULT now()
);

-- Liga o produto de cada plataforma ao produto interno
CREATE TABLE produtos_externos (
    plataforma  text    NOT NULL CHECK (plataforma IN ('kiwify','hotmart','mercadopago')),
    id_externo  text    NOT NULL,
    produto_id  integer NOT NULL REFERENCES produtos(id) ON DELETE CASCADE,
    PRIMARY KEY (plataforma, id_externo)
);

-- ---------- Cupons ----------
CREATE TABLE cupons (
    id              serial PRIMARY KEY,
    codigo          text    NOT NULL UNIQUE CHECK (codigo = upper(codigo) AND codigo ~ '^[A-Z0-9_-]{3,30}$'),
    tipo            text    NOT NULL CHECK (tipo IN ('percentual','fixo')),
    valor           integer NOT NULL CHECK (valor > 0),            -- % ou centavos
    minimo_centavos integer NOT NULL DEFAULT 0,
    valido_ate      timestamptz,
    max_usos        integer CHECK (max_usos > 0),
    usos            integer NOT NULL DEFAULT 0,
    produtos        integer[],                                      -- NULL = vale para todos
    ativo           boolean NOT NULL DEFAULT true,
    criado_em       timestamptz NOT NULL DEFAULT now(),
    CHECK (tipo <> 'percentual' OR valor <= 100)
);

-- ---------- Pedidos (cada venda, de qualquer canal) ----------
CREATE TABLE pedidos (
    id                   uuid PRIMARY KEY,
    usuario_id           uuid REFERENCES usuarios(id) ON DELETE SET NULL,
    email_comprador      text NOT NULL,
    email_normalizado    text NOT NULL,
    nome_comprador       text,
    produto_id           integer NOT NULL REFERENCES produtos(id),
    plataforma           text NOT NULL CHECK (plataforma IN ('kiwify','hotmart','mercadopago','manual','codigo','teste')),
    id_externo           text,                                     -- transação na plataforma
    assinatura_externa   text,
    status               text NOT NULL CHECK (status IN ('pendente','aprovado','reembolsado','chargeback','cancelado','recusado','expirado')),
    valor_centavos       integer NOT NULL DEFAULT 0,
    cupom_id             integer REFERENCES cupons(id),
    observacao           text,
    criado_em            timestamptz NOT NULL DEFAULT now(),
    atualizado_em        timestamptz NOT NULL DEFAULT now(),
    UNIQUE (plataforma, id_externo)
);
CREATE INDEX ix_pedidos_email   ON pedidos (email_normalizado);
CREATE INDEX ix_pedidos_criado  ON pedidos (criado_em DESC);

-- ---------- Acessos (o que libera conteúdo) ----------
-- usuario_id NULL = compra feita com um e-mail que ainda não tem conta;
-- o acesso é ligado à conta quando alguém confirma esse e-mail.
CREATE TABLE acessos (
    id                 bigserial PRIMARY KEY,
    usuario_id         uuid REFERENCES usuarios(id) ON DELETE CASCADE,
    email_normalizado  text NOT NULL,
    produto_id         integer NOT NULL REFERENCES produtos(id),
    pedido_id          uuid REFERENCES pedidos(id),
    inicio             timestamptz NOT NULL DEFAULT now(),
    fim                timestamptz,                               -- NULL = vitalício
    status             text NOT NULL DEFAULT 'ativo' CHECK (status IN ('ativo','revogado')),
    origem             text NOT NULL,                             -- kiwify, hotmart, mercadopago, manual, codigo, teste
    motivo             text,
    criado_por         uuid REFERENCES usuarios(id) ON DELETE SET NULL,
    criado_em          timestamptz NOT NULL DEFAULT now(),
    CHECK (fim IS NULL OR fim > inicio)
);
CREATE INDEX ix_acessos_usuario ON acessos (usuario_id) WHERE status = 'ativo';
CREATE INDEX ix_acessos_email   ON acessos (email_normalizado) WHERE usuario_id IS NULL;

-- ---------- Códigos de acesso (escolas, turmas, presentes) ----------
CREATE TABLE codigos_acesso (
    id          bigserial PRIMARY KEY,
    codigo      text    NOT NULL UNIQUE,
    produto_id  integer NOT NULL REFERENCES produtos(id),
    dias        integer CHECK (dias > 0),                          -- NULL = usa os dias do produto
    lote        text    NOT NULL,
    valido_ate  timestamptz,
    usado_por   uuid REFERENCES usuarios(id) ON DELETE SET NULL,
    usado_em    timestamptz,
    criado_por  uuid REFERENCES usuarios(id) ON DELETE SET NULL,
    criado_em   timestamptz NOT NULL DEFAULT now()
);

-- ---------- Regras de conteúdo (o que é livre, de conta ou pago) ----------
CREATE TABLE regras_conteudo (
    id          serial PRIMARY KEY,
    padrao      text    NOT NULL UNIQUE,        -- caminho; * vale qualquer sequência. Ex.: /projeto-*.html
    nivel       text    NOT NULL CHECK (nivel IN ('livre','conta','pago','equipe','bloqueado')),
    liberacao   text,                           -- para nivel 'pago': a chave que o plano precisa ter
    ordem       integer NOT NULL,               -- a primeira regra que combina vale
    descricao   text    NOT NULL DEFAULT '',
    CHECK (nivel <> 'pago' OR liberacao IS NOT NULL)
);

-- ---------- Webhooks recebidos (tudo guardado, para conferir e reprocessar) ----------
CREATE TABLE webhook_eventos (
    id               bigserial PRIMARY KEY,
    plataforma       text NOT NULL,
    chave            text NOT NULL,             -- evita processar o mesmo aviso duas vezes
    tipo_evento      text,
    recebido_em      timestamptz NOT NULL DEFAULT now(),
    assinatura_valida boolean NOT NULL,
    corpo            text NOT NULL,
    consulta         text,                      -- query string recebida
    processado       boolean NOT NULL DEFAULT false,
    resultado        text,
    tentativas       integer NOT NULL DEFAULT 0,
    UNIQUE (plataforma, chave)
);
CREATE INDEX ix_webhooks_recebido ON webhook_eventos (recebido_em DESC);

-- ---------- Auditoria (quem fez o quê) ----------
CREATE TABLE auditoria (
    id          bigserial PRIMARY KEY,
    quando      timestamptz NOT NULL DEFAULT now(),
    usuario_id  uuid,
    ator        text NOT NULL,                  -- e-mail de quem agiu, ou "sistema", ou "webhook:kiwify"
    acao        text NOT NULL,
    alvo        text,
    detalhes    text,
    ip          text
);
CREATE INDEX ix_auditoria_quando ON auditoria (quando DESC);
CREATE INDEX ix_auditoria_usuario ON auditoria (usuario_id, quando DESC);

-- ---------- Progresso do aluno (as caixas "Pronto quando") ----------
CREATE TABLE progresso (
    usuario_id     uuid NOT NULL REFERENCES usuarios(id) ON DELETE CASCADE,
    chave          text NOT NULL CHECK (length(chave) <= 120),
    valor          text NOT NULL CHECK (length(valor) <= 2000),
    atualizado_em  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (usuario_id, chave)
);
