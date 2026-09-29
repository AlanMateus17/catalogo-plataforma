/*
  admin.js — FERRAMENTA DO SITE (os alunos não precisam copiar este arquivo)
  O painel do administrador. Cada aba só aparece para quem tem a permissão,
  mas quem PROTEGE de verdade é o servidor: cada chamada à API confere a permissão de novo.
  Tudo que vem do banco é colocado na tela com textContent (nunca innerHTML),
  para que um nome como <script> apareça como texto e não rode.
*/
"use strict";

(async function () {
  const P = window.Plataforma;
  const $ = function (id) { return document.getElementById(id); };

  // Cria elementos: h("td", {className: "x"}, "texto", outroElemento)
  function h(tag, props) {
    const e = document.createElement(tag);
    Object.entries(props || {}).forEach(function (kv) {
      if (kv[0].slice(0, 2) === "on") e.addEventListener(kv[0].slice(2), kv[1]);
      else if (kv[0] === "dataset") Object.assign(e.dataset, kv[1]);
      else if (kv[1] !== undefined && kv[1] !== null) e[kv[0]] = kv[1];
    });
    for (let i = 2; i < arguments.length; i++) {
      const f = arguments[i];
      if (f == null || f === false) continue;
      e.append(f instanceof Node ? f : String(f));
    }
    return e;
  }
  const selo = function (t, tipo) { return h("span", { className: "selo" + (tipo ? " selo-" + tipo : "") }, t); };
  const botao = function (t, onclick, classe) { return h("button", { type: "button", className: "botao botao-pequeno " + (classe || ""), onclick: onclick }, t); };
  function tabela(cabecalhos, linhas) {
    return h("div", { className: "tabela-rolagem" },
      h("table", { className: "tabela tabela-admin" },
        h("thead", null, h("tr", null, ...cabecalhos.map(function (c) { return h("th", { scope: "col" }, c); }))),
        h("tbody", null, ...linhas)));
  }
  function campo(rotulo, entrada, ajuda) {
    const id = "c" + Math.random().toString(36).slice(2, 8);
    entrada.id = id;
    return h("div", { className: "campo" }, h("label", { htmlFor: id }, rotulo), entrada, ajuda ? h("span", { className: "ajuda" }, ajuda) : null);
  }
  const entrada = function (props) { return h("input", props); };
  function avisoEl() { const a = h("p", { className: "aviso", hidden: true, tabIndex: -1, role: "status" }); return a; }
  async function acao(aviso, trabalho, sucesso) {
    try {
      const r = await trabalho();
      P.aviso(aviso, (r && (r.mensagem || r.resultado)) || sucesso || "Feito.", "ok");
      return r;
    } catch (err) { P.aviso(aviso, err.message, "erro"); return null; }
  }
  const PLAT = { kiwify: "Kiwify", hotmart: "Hotmart", mercadopago: "Mercado Pago", manual: "Manual", codigo: "Código", teste: "Teste" };
  const STATUS = { aprovado: "ok", pendente: "alerta", reembolsado: "erro", chargeback: "erro", recusado: "erro", cancelado: "erro", expirado: "" };
  const PAPEIS = { aluno: "Aluno", suporte: "Suporte", financeiro: "Financeiro", professor: "Professor", admin: "Administrador" };

  /* ---------- Quem está usando ---------- */
  const eu = await P.eu();
  if (!eu.logado) { location.replace("entrar.html?volta=%2Fadmin.html"); return; }
  const pode = function (p) { return eu.permissoes.indexOf(p) >= 0; };
  $("quem").textContent = eu.nome + " (" + eu.papeis.filter(function (p) { return p !== "aluno"; }).map(function (p) { return PAPEIS[p]; }).join(", ") + ")";
  if (eu.precisaAtivar2fa) {
    $("area").textContent = "";
    $("area").append(h("div", { className: "aviso aviso-erro" }, "Para usar o painel, ative a verificação em duas etapas em ",
      h("a", { href: "conta.html#seguranca" }, "Minha conta → Segurança"), ". É a proteção contra alguém que descubra a sua senha."));
    return;
  }

  let produtos = [];
  async function carregarProdutos() {
    if (!pode("painel.ver")) return;
    produtos = (await P.api("/api/admin/produtos")).map(function (x) { x.produto.externos = x.externos; return x.produto; });
  }
  function seletorProduto(atual) {
    return h("select", null, ...produtos.map(function (p) { return h("option", { value: p.id, selected: p.id === atual }, p.nome + " (" + p.slug + ")"); }));
  }
  await carregarProdutos();

  /* ---------- Abas ---------- */
  const ABAS = [
    ["painel", "Painel", "painel.ver", abaPainel],
    ["usuarios", "Usuários", "usuarios.ver", abaUsuarios],
    ["pedidos", "Vendas", "pedidos.ver", abaPedidos],
    ["produtos", "Produtos e preços", "produtos.gerenciar", abaProdutos],
    ["cupons", "Cupons", "cupons.gerenciar", abaCupons],
    ["codigos", "Códigos de acesso", "codigos.gerenciar", abaCodigos],
    ["conteudo", "O que é pago", "conteudo.gerenciar", abaConteudo],
    ["webhooks", "Avisos das plataformas", "webhooks.ver", abaWebhooks],
    ["auditoria", "Auditoria", "auditoria.ver", abaAuditoria],
  ];
  const barra = $("abas");
  ABAS.filter(function (a) { return pode(a[2]); }).forEach(function (a, i) {
    barra.appendChild(h("button", { type: "button", role: "tab", id: "aba-" + a[0], ariaSelected: "false",
      onclick: function () { abrir(a[0]); } }, a[1]));
  });
  function abrir(nome) {
    const a = ABAS.find(function (x) { return x[0] === nome && pode(x[2]); }) || ABAS.find(function (x) { return pode(x[2]); });
    if (!a) return;
    barra.querySelectorAll("button").forEach(function (b) { b.setAttribute("aria-selected", String(b.id === "aba-" + a[0])); });
    history.replaceState(null, "", "#" + a[0]);
    const area = $("area");
    area.textContent = "";
    area.appendChild(h("p", null, "Carregando..."));
    a[3](area).catch(function (err) { area.textContent = ""; area.appendChild(h("p", { className: "aviso aviso-erro" }, err.message)); });
  }

  /* ---------- Diálogo (detalhes e formulários) ---------- */
  const dialogo = $("dialogo");
  function abrirDialogo(titulo, corpo) {
    $("dialogo-titulo").textContent = titulo;
    const c = $("dialogo-corpo");
    c.textContent = "";
    c.append(corpo);
    if (!dialogo.open) dialogo.showModal();
  }
  $("dialogo-fechar").addEventListener("click", function () { dialogo.close(); });

  /* ================= PAINEL ================= */
  async function abaPainel(area) {
    const d = await P.api("/api/admin/painel");
    const num = function (valor, texto) { return h("div", { className: "admin-numero" }, h("strong", null, valor), h("span", null, texto)); };
    const conversao = d.testesTotal ? Math.round(d.testesQueCompraram / d.testesTotal * 100) + "%" : "—";
    area.textContent = "";
    area.append(h("div", { className: "admin-numeros" },
      num(d.usuarios, "contas"), num(d.novos7, "novas nos últimos 7 dias"), num(d.comAcessoPago, "alunos com plano pago"),
      num(d.testesAtivos, "em teste grátis agora"), num(conversao, "dos testes viraram compra"),
      num(d.reservadosSemConta, "compras esperando a pessoa criar conta"), num(d.naoConfirmados, "contas sem e-mail confirmado")));
    if (d.webhooksComErro > 0) {
      area.append(h("p", { className: "aviso aviso-erro" }, d.webhooksComErro + " aviso(s) de venda com erro. Veja a aba ",
        h("a", { href: "#webhooks", onclick: function (e) { e.preventDefault(); abrir("webhooks"); } }, "Avisos das plataformas"), "."));
    }
    if (d.receitaMes) {
      const total = d.receitaMes.reduce(function (s, x) { return s + x.total; }, 0);
      area.append(h("h2", null, "Este mês: " + P.dinheiro(total)),
        d.receitaMes.length === 0 ? h("p", null, "Nenhuma venda paga neste mês ainda.") : tabela(["Canal", "Vendas", "Total"], d.receitaMes.map(function (x) { return h("tr", null, h("td", null, PLAT[x.plataforma] || x.plataforma), h("td", null, x.vendas), h("td", null, P.dinheiro(x.total))); })),
        h("p", { className: "ajuda" }, "Reembolsos e contestações no mês: " + P.dinheiro(d.reembolsosMes || 0) + ". Valores brutos, antes das taxas das plataformas."));
      if (d.receita12Meses.length) {
        // Sempre 12 colunas (os meses sem venda aparecem com R$ 0), a última é o mês atual
        const porMes = {}; d.receita12Meses.forEach(function (x) { porMes[x.mes] = x.total; });
        const meses = [], hoje = new Date();
        for (let k = 11; k >= 0; k--) {
          const dt = new Date(hoje.getFullYear(), hoje.getMonth() - k, 1);
          const chave = dt.getFullYear() + "-" + String(dt.getMonth() + 1).padStart(2, "0");
          meses.push({ mes: chave, total: porMes[chave] || 0 });
        }
        const maior = Math.max.apply(null, meses.map(function (x) { return x.total; })) || 1;
        const curto = function (v) { return v >= 100000 ? "R$ " + Math.round(v / 100000) + " mil" : v > 0 ? "R$ " + Math.round(v / 100) : ""; };
        area.append(h("h2", null, "Últimos 12 meses"),
          h("div", { className: "grafico-barras", role: "img", ariaLabel: "Faturamento por mês: " + meses.map(function (x) { return x.mes + " " + P.dinheiro(x.total); }).join("; ") },
            ...meses.map(function (x) {
              return h("div", { className: "grafico-coluna", title: x.mes + ": " + P.dinheiro(x.total) },
                h("span", { className: "grafico-valor" }, curto(x.total)),
                h("div", { className: "grafico-barra", style: "height:" + (x.total ? Math.max(3, x.total / maior * 85) : 0) + "%" }));
            })),
          h("div", { className: "grafico-legenda" }, ...meses.map(function (x) { return h("span", null, x.mes.slice(5) + "/" + x.mes.slice(2, 4)); })));
      }
      area.append(h("h2", null, "Últimas vendas"), d.ultimosPedidos.length ? tabelaPedidos(d.ultimosPedidos) : h("p", null, "Nenhuma venda registrada ainda."));
    }
    const integ = await P.api("/api/admin/integracoes");
    area.append(h("h2", null, "Integrações"),
      tabela(["Plataforma", "Situação", "Endereço do webhook (cole na plataforma)"], integ.plataformas.map(function (p) {
        return h("tr", null, h("td", null, PLAT[p.nome]), h("td", null, p.configurada ? selo("Configurada", "ok") : selo("Falta configurar", "alerta")), h("td", null, h("code", null, p.urlWebhook)));
      })),
      h("p", { className: "ajuda" }, "E-mail: " + integ.email + ". Verificação em duas etapas obrigatória para a equipe: " + (integ.exige2faEquipe ? "sim" : "NÃO") + "."));
  }

  function tabelaPedidos(lista) {
    return tabela(["Data", "Comprador", "Produto", "Canal", "Valor", "Situação"], lista.map(function (p) {
      return h("tr", null, h("td", null, P.data(p.criadoEm, true)), h("td", null, p.email), h("td", null, p.produto),
        h("td", null, PLAT[p.plataforma] || p.plataforma), h("td", null, P.dinheiro(p.valor)), h("td", null, selo(p.status, STATUS[p.status])));
    }));
  }

  /* ================= USUÁRIOS ================= */
  async function abaUsuarios(area) {
    const busca = entrada({ type: "search", placeholder: "Nome ou e-mail" });
    const papel = h("select", null, h("option", { value: "" }, "Todos"), ...Object.keys(PAPEIS).map(function (p) { return h("option", { value: p }, PAPEIS[p]); }));
    const lista = h("div");
    let pagina = 1;
    async function carregar(pg) {
      pagina = Math.max(1, pg || 1);
      const r = await P.api("/api/admin/usuarios?busca=" + encodeURIComponent(busca.value) + "&papel=" + papel.value + "&pagina=" + pagina);
      lista.textContent = "";
      lista.append(tabela(["Nome", "E-mail", "Papéis", "Situação", "Criada em", "Último login"], r.itens.map(function (u) {
        return h("tr", { className: "clicavel", tabIndex: 0, onclick: function () { detalheUsuario(u.id); },
          onkeydown: function (e) { if (e.key === "Enter") detalheUsuario(u.id); } },
          h("td", null, u.nome), h("td", null, u.email), h("td", null, u.papeis.map(function (p) { return PAPEIS[p]; }).join(", ")),
          h("td", null, u.bloqueado ? selo("Bloqueado", "erro") : !u.confirmado ? selo("Sem confirmar", "alerta") : u.emTeste ? selo("Em teste", "alerta") : u.temAcesso ? selo("Com plano", "ok") : selo("Grátis")),
          h("td", null, P.data(u.criadoEm)), h("td", null, P.data(u.ultimoLogin, true)));
      })));
      const pag = h("div", { className: "paginacao" }, h("span", null, r.total + " conta(s). Página " + pagina),
        botao("Anterior", function () { if (pagina > 1) carregar(pagina - 1); }, "botao-contorno"),
        botao("Próxima", function () { if (pagina * r.porPagina < r.total) carregar(pagina + 1); }, "botao-contorno"));
      lista.append(pag);
    }
    area.textContent = "";
    area.append(h("form", { className: "admin-barra", onsubmit: function (e) { e.preventDefault(); carregar(1); } },
      campo("Buscar", busca), campo("Papel", papel), h("button", { className: "botao", type: "submit" }, "Buscar")), lista);
    await carregar(1);
  }

  async function detalheUsuario(id) {
    const u = await P.api("/api/admin/usuarios/" + id);
    const aviso = avisoEl();
    const corpo = h("div", { className: "dialogo-corpo" });
    const recarregar = function () { detalheUsuario(id); };

    corpo.append(h("div", { className: "grade-2" },
      h("div", null, h("strong", null, "E-mail: "), u.email, " ", u.confirmado ? selo("confirmado", "ok") : selo("sem confirmar", "alerta")),
      h("div", null, h("strong", null, "Criada em: "), P.data(u.criadoEm, true)),
      h("div", null, h("strong", null, "Último login: "), P.data(u.ultimoLoginEm, true)),
      h("div", null, h("strong", null, "2 etapas: "), u.doisFatores ? selo("ativa", "ok") : selo("desligada")),
      h("div", null, h("strong", null, "Teste grátis: "), u.testeUsadoEm ? "usado em " + P.data(u.testeUsadoEm) : "não usou"),
      h("div", null, h("strong", null, "Termos: "), u.termosVersao ? "versão " + u.termosVersao + " em " + P.data(u.termosAceitosEm) : "—")),
      u.bloqueado ? h("p", { className: "aviso aviso-erro" }, "Bloqueado: " + (u.motivoBloqueio || "")) : "", aviso);

    // Ações da conta
    const acoes = h("div", { className: "acoes-linha" });
    if (pode("usuarios.editar")) {
      if (u.bloqueado) acoes.append(botao("Desbloquear", async function () { if (await acao(aviso, function () { return P.api("/api/admin/usuarios/" + id + "/desbloquear", "POST"); })) recarregar(); }));
      else acoes.append(botao("Bloquear", async function () {
        const motivo = prompt("Motivo do bloqueio (fica registrado):");
        if (motivo && await acao(aviso, function () { return P.api("/api/admin/usuarios/" + id + "/bloquear", "POST", { motivo: motivo }); })) recarregar();
      }, "botao-contorno"));
      if (!u.confirmado) acoes.append(botao("Confirmar e-mail manualmente", async function () { if (await acao(aviso, function () { return P.api("/api/admin/usuarios/" + id + "/confirmar-email", "POST"); })) recarregar(); }, "botao-contorno"));
      acoes.append(botao("Encerrar sessões", function () { acao(aviso, function () { return P.api("/api/admin/usuarios/" + id + "/encerrar-sessoes", "POST"); }, "Sessões encerradas (vale em até 1 minuto)."); }, "botao-contorno"));
    }
    if (pode("papeis.gerenciar") && u.doisFatores) acoes.append(botao("Desligar 2 etapas", async function () {
      const motivo = prompt("Motivo (ex.: perdeu o celular; identidade conferida por vídeo):");
      if (motivo && await acao(aviso, function () { return P.api("/api/admin/usuarios/" + id + "/desativar-2fa", "POST", { motivo: motivo }); })) recarregar();
    }, "botao-contorno"));
    corpo.append(acoes);

    // Papéis
    if (pode("papeis.gerenciar")) {
      const caixas = Object.keys(PAPEIS).filter(function (p) { return p !== "aluno"; }).map(function (p) {
        return h("label", { className: "linha-check" }, h("input", { type: "checkbox", value: p, checked: u.papeis.indexOf(p) >= 0 }), PAPEIS[p]);
      });
      corpo.append(h("section", null, h("h3", null, "Papéis"),
        h("p", { className: "ajuda" }, "Suporte: atende alunos e libera acesso. Financeiro: vendas, cupons e códigos. Professor: define o que é pago. Administrador: tudo."),
        h("div", { className: "acoes-linha" }, ...caixas),
        botao("Salvar papéis", async function () {
          const papeis = caixas.map(function (l) { return l.querySelector("input"); }).filter(function (c) { return c.checked; }).map(function (c) { return c.value; });
          if (await acao(aviso, function () { return P.api("/api/admin/usuarios/" + id + "/papeis", "POST", { papeis: papeis }); }, "Papéis salvos.")) recarregar();
        })));
    }

    // Acessos
    corpo.append(h("section", null, h("h3", null, "Acessos"),
      u.acessos.length ? tabela(["Produto", "Origem", "Início", "Fim", "Situação", ""], u.acessos.map(function (a) {
        return h("tr", null, h("td", null, a.produto), h("td", null, PLAT[a.origem] || a.origem), h("td", null, P.data(a.inicio)), h("td", null, a.fim ? P.data(a.fim) : "vitalício"),
          h("td", null, a.vigente ? selo("vale", "ok") : selo(a.status === "revogado" ? "revogado" : "vencido", a.status === "revogado" ? "erro" : ""), a.motivo ? h("div", { className: "ajuda" }, a.motivo) : null),
          h("td", null, a.vigente && pode("acessos.gerenciar") ? botao("Revogar", async function () {
            const motivo = prompt("Motivo da revogação:");
            if (motivo && await acao(aviso, function () { return P.api("/api/admin/acessos/" + a.id + "/revogar", "POST", { motivo: motivo }); })) recarregar();
          }, "botao-contorno") : null));
      })) : h("p", null, "Nenhum acesso.")));
    if (pode("acessos.gerenciar")) {
      const prod = seletorProduto(), dias = entrada({ type: "number", min: 1, max: 3650, placeholder: "do produto" }), motivo = entrada({ placeholder: "ex.: bolsa, cortesia, correção" });
      corpo.append(h("form", { className: "admin-barra", onsubmit: async function (e) {
        e.preventDefault();
        if (await acao(aviso, function () { return P.api("/api/admin/usuarios/" + id + "/acessos", "POST", { produtoId: Number(prod.value), dias: dias.value ? Number(dias.value) : null, motivo: motivo.value }); }, "Acesso concedido.")) recarregar();
      } }, campo("Conceder produto", prod), campo("Dias", dias), campo("Motivo", motivo), h("button", { className: "botao", type: "submit" }, "Conceder")));
    }

    if (u.pedidos.length) corpo.append(h("section", null, h("h3", null, "Pedidos"), tabelaPedidos(u.pedidos)));
    corpo.append(h("section", null, h("h3", null, "Histórico"), tabela(["Quando", "Quem", "Ação", "IP"], u.historico.map(function (x) {
      return h("tr", null, h("td", null, P.data(x.quando, true)), h("td", null, x.ator), h("td", null, x.acao), h("td", null, x.ip || ""));
    }))));
    abrirDialogo(u.nome, corpo);
  }

  /* ================= VENDAS ================= */
  async function abaPedidos(area) {
    const status = h("select", null, h("option", { value: "" }, "Todas"), ...Object.keys(STATUS).map(function (s) { return h("option", { value: s }, s); }));
    const plat = h("select", null, h("option", { value: "" }, "Todos"), ...Object.keys(PLAT).map(function (s) { return h("option", { value: s }, PLAT[s]); }));
    const busca = entrada({ type: "search", placeholder: "E-mail, nome ou id da transação" });
    const lista = h("div"), aviso = avisoEl();
    let pagina = 1;
    const filtros = function () { return "status=" + status.value + "&plataforma=" + plat.value + "&busca=" + encodeURIComponent(busca.value); };
    async function carregar(pg) {
      pagina = Math.max(1, pg || 1);
      const r = await P.api("/api/admin/pedidos?" + filtros() + "&pagina=" + pagina);
      lista.textContent = "";
      lista.append(tabela(["Data", "Comprador", "Produto", "Canal", "Valor", "Situação", "Transação / observação", ""], r.itens.map(function (p) {
        return h("tr", null, h("td", null, P.data(p.criadoEm, true)), h("td", null, p.email), h("td", null, p.produto), h("td", null, PLAT[p.plataforma] || p.plataforma),
          h("td", null, P.dinheiro(p.valor)), h("td", null, selo(p.status, STATUS[p.status])),
          h("td", null, h("code", null, p.idExterno || ""), p.observacao ? h("div", { className: "ajuda" }, p.observacao) : null),
          h("td", null, p.status === "aprovado" && pode("pedidos.gerenciar") ? botao("Reembolsar", async function () {
            const motivo = prompt("Motivo do reembolso (lembre de devolver o dinheiro na plataforma):");
            if (motivo && await acao(aviso, function () { return P.api("/api/admin/pedidos/" + p.id + "/reembolsar", "POST", { motivo: motivo }); })) carregar(pagina);
          }, "botao-contorno") : null));
      })), h("div", { className: "paginacao" }, h("span", null, r.total + " pedido(s). Página " + pagina),
        botao("Anterior", function () { if (pagina > 1) carregar(pagina - 1); }, "botao-contorno"),
        botao("Próxima", function () { if (pagina * r.porPagina < r.total) carregar(pagina + 1); }, "botao-contorno")));
    }
    area.textContent = "";
    area.append(h("form", { className: "admin-barra", onsubmit: function (e) { e.preventDefault(); carregar(1); } },
      campo("Situação", status), campo("Canal", plat), campo("Buscar", busca), h("button", { className: "botao", type: "submit" }, "Filtrar"),
      h("a", { className: "botao botao-contorno", href: "#", onclick: function (e) { e.preventDefault(); location.href = "/api/admin/pedidos.csv?" + filtros(); } }, "Baixar planilha (CSV)")), aviso, lista);

    if (pode("pedidos.gerenciar")) {
      const email = entrada({ type: "email", required: true }), nome = entrada(), prod = seletorProduto(),
        valor = entrada({ type: "number", min: 0, step: "0.01", required: true }), dias = entrada({ type: "number", min: 1, placeholder: "do produto" }),
        obs = entrada({ required: true, placeholder: "ex.: Pix em 12/10, comprovante no WhatsApp" }), aviso2 = avisoEl();
      area.append(h("details", { className: "painel-caixa" }, h("summary", null, h("strong", null, "Registrar venda direta (Pix, dinheiro, escola)")),
        h("form", { className: "formulario", onsubmit: async function (e) {
          e.preventDefault();
          if (await acao(aviso2, function () { return P.api("/api/admin/pedidos/manual", "POST", { email: email.value, nome: nome.value, produtoId: Number(prod.value),
            valorCentavos: Math.round(Number(valor.value) * 100), observacao: obs.value, dias: dias.value ? Number(dias.value) : null }); })) { e.target.reset(); carregar(1); }
        } }, h("div", { className: "grade-form" }, campo("E-mail do aluno", email), campo("Nome", nome), campo("Produto", prod), campo("Valor recebido (R$)", valor),
          campo("Dias de acesso", dias, "Vazio = o padrão do produto"), campo("Como foi pago", obs)), h("button", { className: "botao", type: "submit" }, "Registrar e liberar"), aviso2)));
    }
    await carregar(1);
  }

  /* ================= PRODUTOS ================= */
  async function abaProdutos(area) {
    await carregarProdutos();
    area.textContent = "";
    area.append(h("p", null, "Os preços e textos da página de planos vêm daqui. Para vender pela Kiwify ou Hotmart, cole o link de checkout do produto e cadastre o id dele (aparece no webhook de teste) em \"Ids nas plataformas\"."),
      botao("Novo produto", function () { editarProduto(null); }),
      tabela(["Ordem", "Produto", "Tipo", "Preço", "Acesso", "Libera", "Na página de planos", ""], produtos.map(function (p) {
        return h("tr", null, h("td", null, p.ordem), h("td", null, h("strong", null, p.nome), h("div", { className: "ajuda" }, p.slug)), h("td", null, p.tipo),
          h("td", null, P.dinheiro(p.precoCentavos)), h("td", null, p.diasAcesso ? p.diasAcesso + " dias" : "vitalício"), h("td", null, p.liberacoes.join(", ")),
          h("td", null, p.ativo ? selo("sim", "ok") : selo("não")), h("td", null, botao("Editar", function () { editarProduto(p); }, "botao-contorno")));
      })));
  }

  function editarProduto(p) {
    p = p || { slug: "", nome: "", descricao: "", tipo: "unico", precoCentavos: 0, precoDeCentavos: null, parcelas: 1, diasAcesso: 365, liberacoes: ["modulos", "projetos"],
      beneficios: [], urlKiwify: "", urlHotmart: "", mercadoPago: false, ativo: false, destaque: false, ordem: 50, externos: [] };
    const f = {
      nome: entrada({ value: p.nome, required: true }), slug: entrada({ value: p.slug, required: true, pattern: "[a-z0-9-]+" }),
      tipo: h("select", null, ...["assinatura", "unico", "licenca", "teste"].map(function (t) { return h("option", { value: t, selected: t === p.tipo }, t); })),
      preco: entrada({ type: "number", step: "0.01", min: 0, value: p.precoCentavos / 100 }), de: entrada({ type: "number", step: "0.01", min: 0, value: p.precoDeCentavos ? p.precoDeCentavos / 100 : "" }),
      parcelas: entrada({ type: "number", min: 1, max: 12, value: p.parcelas }), dias: entrada({ type: "number", min: 1, value: p.diasAcesso || "" }),
      lib: entrada({ value: p.liberacoes.join(", ") }), desc: h("textarea", { value: p.descricao }), ben: h("textarea", { value: p.beneficios.join("\n") }),
      kw: entrada({ type: "url", value: p.urlKiwify || "" }), hm: entrada({ type: "url", value: p.urlHotmart || "" }),
      mp: h("input", { type: "checkbox", checked: p.mercadoPago }), ativo: h("input", { type: "checkbox", checked: p.ativo }), dest: h("input", { type: "checkbox", checked: p.destaque }),
      ordem: entrada({ type: "number", value: p.ordem }),
    };
    const aviso = avisoEl();
    const corpo = h("div", { className: "dialogo-corpo" }, h("form", { className: "formulario", onsubmit: async function (e) {
      e.preventDefault();
      const dados = { nome: f.nome.value, slug: f.slug.value, descricao: f.desc.value, tipo: f.tipo.value, precoCentavos: Math.round(Number(f.preco.value) * 100),
        precoDeCentavos: f.de.value ? Math.round(Number(f.de.value) * 100) : null, parcelas: Number(f.parcelas.value), diasAcesso: f.dias.value ? Number(f.dias.value) : null,
        liberacoes: f.lib.value.split(","), beneficios: f.ben.value.split("\n"), urlKiwify: f.kw.value, urlHotmart: f.hm.value,
        mercadoPago: f.mp.checked, ativo: f.ativo.checked, destaque: f.dest.checked, ordem: Number(f.ordem.value) };
      const r = await acao(aviso, function () { return p.id ? P.api("/api/admin/produtos/" + p.id, "PUT", dados) : P.api("/api/admin/produtos", "POST", dados); }, "Produto salvo.");
      if (r) { await carregarProdutos(); abrir("produtos"); }
    } },
      h("div", { className: "grade-form" }, campo("Nome", f.nome), campo("Slug (endereço)", f.slug, "minúsculas e hífen"), campo("Tipo", f.tipo, "assinatura = cobrança recorrente"),
        campo("Preço (R$)", f.preco), campo("Preço \"de\" (R$)", f.de, "opcional, aparece riscado"), campo("Parcelas", f.parcelas), campo("Dias de acesso", f.dias, "vazio = vitalício"),
        campo("Libera", f.lib, "separadas por vírgula: modulos, projetos"), campo("Ordem na página", f.ordem)),
      campo("Descrição", f.desc), campo("Benefícios (um por linha)", f.ben),
      h("div", { className: "grade-form" }, campo("Link de checkout Kiwify", f.kw), campo("Link de checkout Hotmart", f.hm)),
      h("div", { className: "acoes-linha" }, h("label", { className: "linha-check" }, f.mp, "Vender pelo Mercado Pago do site"), h("label", { className: "linha-check" }, f.ativo, "Mostrar na página de planos"),
        h("label", { className: "linha-check" }, f.dest, "Destaque (\"Mais escolhido\")")),
      h("button", { className: "botao", type: "submit" }, "Salvar"), aviso));

    if (p.id) {
      const plat = h("select", null, h("option", { value: "kiwify" }, "Kiwify"), h("option", { value: "hotmart" }, "Hotmart"));
      const idExt = entrada({ placeholder: "id do produto na plataforma" });
      corpo.append(h("section", null, h("h3", null, "Ids nas plataformas"),
        h("p", { className: "ajuda" }, "Quando chega um aviso de venda, o sistema usa este id para saber qual produto liberar. Para liberar só uma oferta/plano, use \"idDoProduto:idDaOferta\"."),
        p.externos.length ? tabela(["Plataforma", "Id", ""], p.externos.map(function (x) {
          return h("tr", null, h("td", null, PLAT[x.plataforma]), h("td", null, h("code", null, x.idExterno)), h("td", null, botao("Remover", async function () {
            await acao(aviso, function () { return P.api("/api/admin/produtos/" + p.id + "/externos?plataforma=" + x.plataforma + "&idExterno=" + encodeURIComponent(x.idExterno), "DELETE"); });
            await carregarProdutos(); editarProduto(produtos.find(function (q) { return q.id === p.id; }));
          }, "botao-contorno")));
        })) : h("p", null, "Nenhum id cadastrado."),
        h("form", { className: "admin-barra", onsubmit: async function (e) {
          e.preventDefault();
          await acao(aviso, function () { return P.api("/api/admin/produtos/" + p.id + "/externos", "POST", { plataforma: plat.value, idExterno: idExt.value }); });
          await carregarProdutos(); editarProduto(produtos.find(function (q) { return q.id === p.id; }));
        } }, campo("Plataforma", plat), campo("Id", idExt), h("button", { className: "botao", type: "submit" }, "Adicionar"))));
    }
    abrirDialogo(p.id ? "Editar: " + p.nome : "Novo produto", corpo);
  }

  /* ================= CUPONS ================= */
  async function abaCupons(area) {
    const lista = await P.api("/api/admin/cupons");
    const aviso = avisoEl();
    const f = { codigo: entrada({ required: true, placeholder: "ex.: BEMVINDO" }), tipo: h("select", null, h("option", { value: "percentual" }, "% de desconto"), h("option", { value: "fixo" }, "R$ de desconto")),
      valor: entrada({ type: "number", min: 1, required: true }), minimo: entrada({ type: "number", min: 0, step: "0.01", value: 0 }),
      validade: entrada({ type: "date" }), max: entrada({ type: "number", min: 1, placeholder: "sem limite" }) };
    area.textContent = "";
    area.append(h("p", null, "Cupons valem no pagamento pelo Mercado Pago do site. Na Kiwify e na Hotmart, crie o cupom no painel delas."),
      h("form", { className: "admin-barra", onsubmit: async function (e) {
        e.preventDefault();
        const valor = f.tipo.value === "fixo" ? Math.round(Number(f.valor.value) * 100) : Number(f.valor.value);
        if (await acao(aviso, function () { return P.api("/api/admin/cupons", "POST", { codigo: f.codigo.value, tipo: f.tipo.value, valor: valor,
          minimoCentavos: Math.round(Number(f.minimo.value) * 100), validoAte: f.validade.value ? f.validade.value + "T23:59:59-03:00" : null,
          maxUsos: f.max.value ? Number(f.max.value) : null, ativo: true }); }, "Cupom criado.")) abrir("cupons");
      } }, campo("Código", f.codigo), campo("Tipo", f.tipo), campo("Valor", f.valor, "% ou R$"), campo("Compra mínima (R$)", f.minimo), campo("Vale até", f.validade), campo("Máximo de usos", f.max),
        h("button", { className: "botao", type: "submit" }, "Criar")), aviso,
      tabela(["Código", "Desconto", "Mínimo", "Vale até", "Usos", "Ativo", ""], lista.map(function (c) {
        return h("tr", null, h("td", null, h("code", null, c.codigo)), h("td", null, c.tipo === "percentual" ? c.valor + "%" : P.dinheiro(c.valor)), h("td", null, P.dinheiro(c.minimo)),
          h("td", null, c.validoAte ? P.data(c.validoAte) : "—"), h("td", null, c.usos + (c.maxUsos ? " de " + c.maxUsos : "")), h("td", null, c.ativo ? selo("sim", "ok") : selo("não")),
          h("td", null, botao(c.ativo ? "Desativar" : "Ativar", async function () {
            if (await acao(aviso, function () { return P.api("/api/admin/cupons?id=" + c.id, "POST", { codigo: c.codigo, tipo: c.tipo, valor: c.valor, minimoCentavos: c.minimo,
              validoAte: c.validoAte, maxUsos: c.maxUsos, produtos: c.produtos, ativo: !c.ativo }); })) abrir("cupons");
          }, "botao-contorno")));
      })));
  }

  /* ================= CÓDIGOS DE ACESSO ================= */
  async function abaCodigos(area) {
    const lotes = await P.api("/api/admin/codigos");
    const aviso = avisoEl(), saida = h("div");
    const f = { prod: seletorProduto((produtos.find(function (p) { return p.slug === "licenca-escola"; }) || {}).id), qtd: entrada({ type: "number", min: 1, max: 500, value: 30 }),
      dias: entrada({ type: "number", min: 1, placeholder: "do produto" }), lote: entrada({ required: true, placeholder: "ex.: Escola X — 3º ano A — 2027" }), validade: entrada({ type: "date" }) };
    area.textContent = "";
    area.append(h("p", null, "Para escolas, turmas, sorteios e presentes: gere um lote, baixe a planilha e entregue um código por aluno. Cada código vale uma vez."),
      h("form", { className: "admin-barra", onsubmit: async function (e) {
        e.preventDefault();
        const r = await acao(aviso, function () { return P.api("/api/admin/codigos/gerar", "POST", { produtoId: Number(f.prod.value), quantidade: Number(f.qtd.value),
          dias: f.dias.value ? Number(f.dias.value) : null, lote: f.lote.value, validoAte: f.validade.value ? f.validade.value + "T23:59:59-03:00" : null }); }, "Códigos gerados.");
        if (r) { saida.textContent = ""; saida.append(h("pre", { className: "bloco-codigo" }, r.codigos.join("\n"))); }
      } }, campo("Produto", f.prod), campo("Quantidade", f.qtd), campo("Dias de acesso", f.dias), campo("Nome do lote", f.lote), campo("Resgatar até", f.validade),
        h("button", { className: "botao", type: "submit" }, "Gerar")), aviso, saida,
      tabela(["Lote", "Produto", "Usados", "Criado em", "Resgatar até", ""], lotes.map(function (l) {
        return h("tr", null, h("td", null, l.lote), h("td", null, l.produto), h("td", null, l.usados + " de " + l.total), h("td", null, P.data(l.criadoEm)),
          h("td", null, l.validoAte ? P.data(l.validoAte) : "—"), h("td", null, h("a", { className: "botao botao-pequeno botao-contorno", href: "/api/admin/codigos/lote.csv?lote=" + encodeURIComponent(l.lote) }, "Baixar CSV")));
      })));
  }

  /* ================= O QUE É PAGO ================= */
  async function abaConteudo(area) {
    const regras = await P.api("/api/admin/conteudo");
    const aviso = avisoEl();
    const NIVEIS = { livre: "Livre (todos)", conta: "Conta grátis", pago: "Pago", equipe: "Só equipe", bloqueado: "Nunca servido" };
    const teste = entrada({ placeholder: "/projeto-3.html" }), resultado = h("p", { className: "aviso", hidden: true });
    const f = { padrao: entrada({ required: true, placeholder: "/modulo-5-*" }), nivel: h("select", null, ...Object.keys(NIVEIS).map(function (n) { return h("option", { value: n }, NIVEIS[n]); })),
      lib: entrada({ placeholder: "modulos" }), ordem: entrada({ type: "number", value: 60 }), desc: entrada() };
    area.textContent = "";
    area.append(h("p", null, "Cada página do site passa por estas regras, de cima para baixo: a primeira que combina com o endereço decide. * vale qualquer sequência. Página sem regra não abre (404): quando criar uma página nova, crie a regra dela."),
      h("form", { className: "admin-barra", onsubmit: async function (e) {
        e.preventDefault();
        const r = await P.api("/api/admin/conteudo/testar", "POST", { caminho: teste.value });
        P.aviso(resultado, (r.regra ? "Regra " + r.regra.padrao + ": " : "") + r.resultado, r.regra ? "ok" : "erro");
      } }, campo("Testar um endereço", teste), h("button", { className: "botao", type: "submit" }, "Testar")), resultado,
      h("form", { className: "admin-barra", onsubmit: async function (e) {
        e.preventDefault();
        if (await acao(aviso, function () { return P.api("/api/admin/conteudo", "POST", { padrao: f.padrao.value, nivel: f.nivel.value, liberacao: f.lib.value, ordem: Number(f.ordem.value), descricao: f.desc.value }); }, "Regra criada.")) abrir("conteudo");
      } }, campo("Caminho", f.padrao), campo("Nível", f.nivel), campo("Liberação (se pago)", f.lib), campo("Ordem", f.ordem), campo("Descrição", f.desc), h("button", { className: "botao", type: "submit" }, "Criar regra")), aviso,
      tabela(["Ordem", "Caminho", "Nível", "Liberação", "Descrição", ""], regras.map(function (r) {
        return h("tr", null, h("td", null, r.ordem), h("td", null, h("code", null, r.padrao)), h("td", null, selo(NIVEIS[r.nivel], r.nivel === "pago" ? "alerta" : r.nivel === "livre" ? "ok" : "")),
          h("td", null, r.liberacao || ""), h("td", null, r.descricao), h("td", null, botao("Apagar", async function () {
            if (confirm("Apagar a regra " + r.padrao + "? Páginas que só combinavam com ela deixam de abrir.")) { await acao(aviso, function () { return P.api("/api/admin/conteudo/" + r.id, "DELETE"); }); abrir("conteudo"); }
          }, "botao-contorno")));
      })));
  }

  /* ================= WEBHOOKS ================= */
  async function abaWebhooks(area) {
    const soErro = h("input", { type: "checkbox" }), lista = h("div"), aviso = avisoEl();
    async function carregar() {
      const r = await P.api("/api/admin/webhooks" + (soErro.checked ? "?comErro=true" : ""));
      lista.textContent = "";
      lista.append(tabela(["Recebido", "Plataforma", "Evento", "Assinatura", "Resultado", ""], r.itens.map(function (w) {
        return h("tr", null, h("td", null, P.data(w.recebidoEm, true)), h("td", null, PLAT[w.plataforma] || w.plataforma), h("td", null, w.tipo || ""),
          h("td", null, w.assinaturaValida ? selo("válida", "ok") : selo("inválida", "erro")),
          h("td", null, !w.processado && w.assinaturaValida ? selo("com erro", "erro") : null, " ", w.resultado || ""),
          h("td", null, h("div", { className: "acoes-linha" }, botao("Ver", async function () {
            const d = await P.api("/api/admin/webhooks/" + w.id);
            let bonito = d.corpo;
            try { bonito = JSON.stringify(JSON.parse(d.corpo), null, 2); } catch (e) {}
            abrirDialogo("Aviso " + d.id + " — " + (PLAT[d.plataforma] || d.plataforma), h("div", { className: "dialogo-corpo" }, h("p", null, d.resultado || ""), h("pre", null, bonito)));
          }, "botao-contorno"), w.assinaturaValida && pode("webhooks.reprocessar") ? botao("Reprocessar", async function () {
            await acao(aviso, function () { return P.api("/api/admin/webhooks/" + w.id + "/reprocessar", "POST"); });
            carregar();
          }, "botao-contorno") : null)));
      })));
    }
    area.textContent = "";
    area.append(h("p", null, "Todo aviso de venda que chega fica guardado aqui. Se um aviso deu erro (ex.: produto sem id cadastrado), corrija a causa e clique em Reprocessar."),
      h("label", { className: "linha-check" }, soErro, "Mostrar só os que deram erro"), aviso, lista);
    soErro.addEventListener("change", carregar);
    await carregar();
  }

  /* ================= AUDITORIA ================= */
  async function abaAuditoria(area) {
    const busca = entrada({ type: "search", placeholder: "e-mail, ação (ex.: papeis, venda, login.falhou)" }), lista = h("div");
    let pagina = 1;
    async function carregar(pg) {
      pagina = Math.max(1, pg || 1);
      const r = await P.api("/api/admin/auditoria?busca=" + encodeURIComponent(busca.value) + "&pagina=" + pagina);
      lista.textContent = "";
      lista.append(tabela(["Quando", "Quem", "Ação", "Sobre", "Detalhes", "IP"], r.itens.map(function (x) {
        return h("tr", null, h("td", null, P.data(x.quando, true)), h("td", null, x.ator), h("td", null, h("code", null, x.acao)), h("td", null, x.alvo || ""),
          h("td", null, h("span", { className: "ajuda" }, (x.detalhes || "").slice(0, 200))), h("td", null, x.ip || ""));
      })), h("div", { className: "paginacao" }, h("span", null, r.total + " registro(s). Página " + pagina),
        botao("Anterior", function () { if (pagina > 1) carregar(pagina - 1); }, "botao-contorno"),
        botao("Próxima", function () { if (pagina * r.porPagina < r.total) carregar(pagina + 1); }, "botao-contorno")));
    }
    area.textContent = "";
    area.append(h("form", { className: "admin-barra", onsubmit: function (e) { e.preventDefault(); carregar(1); } }, campo("Buscar", busca), h("button", { className: "botao", type: "submit" }, "Buscar")), lista);
    await carregar(1);
  }

  abrir((location.hash || "#painel").slice(1));
})();
