# Plataforma do Catálogo do HTML

Sistema que vende e protege o curso do [Catálogo do HTML](https://github.com/AlanMateus17/catalogo-html): contas de aluno, login com 2 etapas, papéis, painel do administrador, planos, vendas e liberação automática do conteúdo pago.

- **Stack atual:** C# / ASP.NET Core + PostgreSQL + Docker, HTTPS pelo Caddy. Desde a ADR 0017 do dev-setup, o .NET é carta de reserva: este repositório está com status **revisar** (decidir se mantém, migra para TypeScript ou funde com o AM Gestão).
- **Como rodar e operar:** [`LEIA-ME.md`](LEIA-ME.md).
- **Segredos:** nunca no repositório; use o `.env.exemplo` como modelo e guarde os valores no KeePassXC.
