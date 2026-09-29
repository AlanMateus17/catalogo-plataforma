-- 004 — Teste grátis: uma vez por e-mail "de verdade".
-- joao+1@gmail.com, j.o.a.o@gmail.com e joao@gmail.com chegam na mesma caixa: contam como um só.
-- Guarda só o hash (SHA-256) do e-mail canônico, e ele fica mesmo se a conta for excluída
-- (senão bastaria excluir e criar de novo para ganhar outro teste).
CREATE TABLE testes_usados (
    chave     text PRIMARY KEY,           -- SHA-256 do e-mail canônico, em hexadecimal
    usado_em  timestamptz NOT NULL DEFAULT now()
);
