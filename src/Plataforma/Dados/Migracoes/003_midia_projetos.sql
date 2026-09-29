-- 003 — Vídeos e imagens do "resultado esperado" dos projetos.
-- Ficam em /midia-projetos/. Os do Projeto 1 são livres (amostra); os outros, só com o plano.
INSERT INTO regras_conteudo (padrao, nivel, liberacao, ordem, descricao) VALUES
  ('/midia-projetos/projeto-1-*', 'livre', NULL,       44, 'Imagens do resultado do Projeto 1'),
  ('/midia-projetos/projeto-1.*', 'livre', NULL,       45, 'Vídeo do resultado do Projeto 1'),
  ('/midia-projetos/*',           'pago',  'projetos', 46, 'Vídeos e imagens dos outros projetos');
