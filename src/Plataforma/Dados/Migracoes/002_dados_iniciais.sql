-- =====================================================================
-- 002 — DADOS INICIAIS: papéis, produtos de exemplo e regras de conteúdo
-- Preços e textos são EXEMPLOS: ajuste no painel do administrador.
-- =====================================================================

INSERT INTO papeis (id, nome) VALUES
  ('aluno',      'Aluno'),
  ('suporte',    'Suporte'),
  ('financeiro', 'Financeiro'),
  ('professor',  'Professor'),
  ('admin',      'Administrador');

INSERT INTO produtos (slug, nome, descricao, tipo, preco_centavos, preco_de_centavos, parcelas, dias_acesso, liberacoes, beneficios, ativo, destaque, ordem) VALUES
  ('teste-gratis', 'Teste grátis de 7 dias',
   'Acesso completo por 7 dias, sem cartão. Uma vez por conta.',
   'teste', 0, NULL, 1, 7, '{modulos,projetos}', '{}', false, false, 0),

  ('plano-mensal', 'Formação — mensal',
   'Todos os módulos e projetos enquanto a assinatura estiver ativa.',
   'assinatura', 4990, NULL, 1, 30, '{modulos,projetos}',
   '{"Todos os módulos da formação","Todos os projetos, com resultado esperado","Progresso salvo na sua conta","Cancele quando quiser"}',
   true, false, 10),

  ('plano-anual', 'Formação — 12 meses',
   'Um ano inteiro de formação pelo preço de 8 meses.',
   'unico', 39700, 59880, 12, 365, '{modulos,projetos}',
   '{"Tudo do plano mensal","Um ano de acesso","Os módulos novos entram no seu plano","Garantia de 7 dias"}',
   true, true, 20),

  ('pacote-projetos', 'Pacote de projetos',
   'Só os projetos, para quem já estuda por outro lugar e quer praticar.',
   'unico', 19700, NULL, 12, 365, '{projetos}',
   '{"Todos os projetos, com enunciado, dicas e resultado esperado","Os projetos novos entram no pacote conforme são lançados","Um ano de acesso"}',
   true, false, 30),

  ('licenca-escola', 'Licença para escolas e turmas',
   'Códigos de acesso para uma turma inteira. Venda direta: fale com o professor.',
   'licenca', 0, NULL, 1, 365, '{modulos,projetos}', '{}', false, false, 40);

-- A primeira regra que combina com o caminho vale (ordem crescente).
-- Níveis: livre (todo mundo), conta (cadastro grátis), pago (precisa da liberação),
--         equipe (só quem tem papel de equipe), bloqueado (nunca é servido).
INSERT INTO regras_conteudo (padrao, nivel, liberacao, ordem, descricao) VALUES
  -- Arquivos que não são do site
  ('/ferramentas/*',          'bloqueado', NULL, 10, 'Ferramentas do professor'),
  ('/trechos/*',              'bloqueado', NULL, 11, 'Fontes dos códigos (o site usa os gerados em js/)'),
  ('*.py',                    'bloqueado', NULL, 12, 'Scripts'),
  ('/LEIA-ME.txt',            'bloqueado', NULL, 13, 'Instruções do professor'),

  -- Equipe
  ('/admin.html',             'equipe',    NULL, 20, 'Painel do administrador'),

  -- Precisa de conta grátis
  ('/conta.html',             'conta',     NULL, 30, 'Minha conta'),
  ('/passo-5-*',              'conta',     NULL, 31, 'Passos 5 a 10: conta grátis'),
  ('/passo-6-*',              'conta',     NULL, 32, ''),
  ('/passo-7-*',              'conta',     NULL, 33, ''),
  ('/passo-8-*',              'conta',     NULL, 34, ''),
  ('/passo-9-*',              'conta',     NULL, 35, ''),
  ('/passo-10-*',             'conta',     NULL, 36, ''),
  ('/doc-html.html',          'conta',     NULL, 37, 'Amostra do módulo 2'),

  -- Livres (amostra que atrai)
  ('/projeto-1.html',         'livre',     NULL, 40, 'Projeto 1 completo: amostra grátis'),

  -- Pagos
  ('/projeto-*.html',         'pago',      'projetos', 50, 'Projetos 2 a 12'),
  ('/doc-*.html',             'pago',      'modulos',  51, 'Capítulos dos módulos'),
  ('/modulo-*',               'pago',      'modulos',  52, 'Módulos futuros'),

  -- Todo o resto que for .html e não estiver acima: livre só se estiver na lista abaixo
  ('/',                       'livre',     NULL, 90, ''),
  ('/index.html',             'livre',     NULL, 91, 'Catálogo'),
  ('/codigos.html',           'livre',     NULL, 92, ''),
  ('/dicionario.html',        'livre',     NULL, 93, ''),
  ('/ia.html',                'livre',     NULL, 94, ''),
  ('/sobre.html',             'livre',     NULL, 95, ''),
  ('/verificador.html',       'livre',     NULL, 96, ''),
  ('/documentacao.html',      'livre',     NULL, 97, 'Vitrine dos módulos'),
  ('/projetos.html',          'livre',     NULL, 98, 'Mapa dos projetos'),
  ('/passo-a-passo.html',     'livre',     NULL, 99, ''),
  ('/passo-*',                'livre',     NULL, 100, 'Passos 1 a 4'),
  ('/planos.html',            'livre',     NULL, 101, ''),
  ('/entrar.html',            'livre',     NULL, 102, ''),
  ('/cadastro.html',          'livre',     NULL, 103, ''),
  ('/confirmar-email.html',   'livre',     NULL, 104, ''),
  ('/esqueci-senha.html',     'livre',     NULL, 105, ''),
  ('/redefinir-senha.html',   'livre',     NULL, 106, ''),
  ('/termos.html',            'livre',     NULL, 107, ''),
  ('/privacidade.html',       'livre',     NULL, 108, ''),
  ('/compra-segura.html',     'livre',     NULL, 109, ''),
  ('/demos/*',                'livre',     NULL, 110, 'Demos dos códigos'),
  ('/css/*',                  'livre',     NULL, 120, ''),
  ('/js/*',                   'livre',     NULL, 121, ''),
  ('/img/*',                  'livre',     NULL, 122, ''),
  ('/midia/*',                'livre',     NULL, 123, ''),
  ('/docs/*',                 'livre',     NULL, 124, ''),
  ('/favicon.ico',            'livre',     NULL, 125, '');
-- Um caminho que não combina com nenhuma regra NÃO é servido (fica 404).
-- Assim, uma página nova nunca vaza por esquecimento: crie a regra dela.
