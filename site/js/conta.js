/*
  conta.js — FERRAMENTA DO SITE (os alunos não precisam copiar este arquivo)
  A página "Minha conta": plano, teste grátis, código de acesso, pedidos,
  senha, verificação em duas etapas e os seus dados (LGPD).
*/
"use strict";

(async function () {
  const P = window.Plataforma;
  const $ = function (id) { return document.getElementById(id); };
  const NOMES_PLATAFORMA = { kiwify: "Kiwify", hotmart: "Hotmart", mercadopago: "Mercado Pago", manual: "Venda direta", codigo: "Código de acesso", teste: "Teste grátis" };
  const NOMES_STATUS = { aprovado: ["Aprovado", "ok"], pendente: ["Aguardando pagamento", "alerta"], reembolsado: ["Reembolsado", "erro"],
    chargeback: ["Contestado", "erro"], cancelado: ["Cancelado", "erro"], recusado: ["Recusado", "erro"], expirado: ["Expirado", ""] };

  let eu = await P.eu();
  if (!eu.logado) { location.replace("entrar.html?volta=%2Fconta.html"); return; }

  function selo(texto, tipo) {
    const s = document.createElement("span");
    s.className = "selo" + (tipo ? " selo-" + tipo : "");
    s.textContent = texto;
    return s;
  }

  // Retorno do Mercado Pago
  const compra = P.param("compra");
  if (compra === "aprovada") P.aviso($("aviso-topo"), "Pagamento aprovado! A liberação chega em até alguns minutos (atualize a página).", "ok");
  if (compra === "pendente") P.aviso($("aviso-topo"), "Pagamento em análise ou aguardando Pix/boleto. Liberamos assim que ele for aprovado.", "info");
  if (eu.precisaAtivar2fa) P.aviso($("aviso-topo"), "Você faz parte da equipe: ative a verificação em duas etapas (abaixo) para usar o painel.", "info");

  /* ---------- Plano e acessos ---------- */
  function desenharAcessos() {
    $("ola").textContent = "Olá, " + eu.nome.split(" ")[0] + "!";
    const lista = $("lista-acessos");
    lista.textContent = "";
    const vigentes = eu.acessos.filter(function (a) { return a.vigente; });
    if (vigentes.length === 0) {
      const li = document.createElement("li");
      li.textContent = "Você está no plano grátis: Catálogo, Códigos, Dicionário, a trilha Passo a passo completa e o Projeto 1.";
      lista.appendChild(li);
    }
    vigentes.forEach(function (a) {
      const li = document.createElement("li");
      li.className = "acesso-item";
      const nome = document.createElement("div");
      const forte = document.createElement("strong");
      forte.textContent = a.produto;
      const quando = document.createElement("span");
      quando.textContent = a.fim ? "até " + P.data(a.fim) : "sem data para acabar";
      nome.append(forte, quando);
      li.append(nome, selo(NOMES_PLATAFORMA[a.origem] || a.origem, "ok"));
      lista.appendChild(li);
    });
    $("caixa-teste").hidden = !eu.testeDisponivel;
    $("link-planos").hidden = vigentes.length > 0 && !vigentes.every(function (a) { return a.origem === "teste"; });
  }
  desenharAcessos();

  async function recarregar() {
    P.renovarSessao();
    eu = await P.eu();
    desenharAcessos();
    desenharSeguranca();
  }

  $("btn-teste").addEventListener("click", async function () {
    try {
      const r = await P.carregando(this, function () { return P.api("/api/conta/teste-gratis", "POST"); });
      P.aviso($("aviso-teste"), r.mensagem, "ok");
      await recarregar();
    } catch (err) { P.aviso($("aviso-teste"), err.message, "erro"); }
  });

  $("form-codigo").addEventListener("submit", async function (e) {
    e.preventDefault();
    try {
      const r = await P.carregando(this.querySelector("button"), function () { return P.api("/api/conta/resgatar-codigo", "POST", { codigo: $("codigo-acesso").value }); });
      P.aviso($("aviso-codigo"), r.mensagem, "ok");
      $("codigo-acesso").value = "";
      await recarregar();
    } catch (err) { P.aviso($("aviso-codigo"), err.message, "erro"); }
  });

  /* ---------- Pedidos ---------- */
  P.api("/api/conta/pedidos").then(function (pedidos) {
    const corpo = $("pedidos");
    if (pedidos.length === 0) { $("sem-pedidos").hidden = false; $("tabela-pedidos").hidden = true; return; }
    pedidos.forEach(function (p) {
      const tr = document.createElement("tr");
      const st = NOMES_STATUS[p.status] || [p.status, ""];
      [P.data(p.criadoEm), p.produto, NOMES_PLATAFORMA[p.plataforma] || p.plataforma, P.dinheiro(p.valor)].forEach(function (t) {
        const td = document.createElement("td"); td.textContent = t; tr.appendChild(td);
      });
      const td = document.createElement("td"); td.appendChild(selo(st[0], st[1])); tr.appendChild(td);
      corpo.appendChild(tr);
    });
  });

  /* ---------- Nome e senha ---------- */
  $("nome").value = eu.nome;
  $("email-atual").textContent = eu.email;
  $("form-nome").addEventListener("submit", async function (e) {
    e.preventDefault();
    try {
      await P.carregando(this.querySelector("button"), function () { return P.api("/api/conta/alterar-nome", "POST", { nome: $("nome").value }); });
      P.aviso($("aviso-nome"), "Nome atualizado.", "ok");
      eu.nome = $("nome").value; desenharAcessos();
    } catch (err) { P.aviso($("aviso-nome"), err.message, "erro"); }
  });

  $("form-senha").addEventListener("submit", async function (e) {
    e.preventDefault();
    try {
      const r = await P.carregando(this.querySelector("button"), function () {
        return P.api("/api/conta/alterar-senha", "POST", { atual: $("senha-atual").value, nova: $("senha-nova").value });
      });
      P.aviso($("aviso-senha"), r.mensagem, "ok");
      this.reset();
      P.renovarSessao();
    } catch (err) { P.aviso($("aviso-senha"), err.message, "erro"); }
  });

  $("sair-todos").addEventListener("click", async function () {
    try {
      const r = await P.carregando(this, function () { return P.api("/api/conta/sair-de-todos", "POST"); });
      P.renovarSessao();
      P.aviso($("aviso-senha"), r.mensagem, "ok");
    } catch (err) { P.aviso($("aviso-senha"), err.message, "erro"); }
  });

  $("sair").addEventListener("click", async function () {
    await P.api("/api/conta/sair", "POST").catch(function () {});
    P.renovarSessao();
    location.href = "index.html";
  });

  /* ---------- Verificação em duas etapas ---------- */
  function mostrarCodigos(codigos) {
    const caixa = $("codigos-recuperacao");
    caixa.textContent = "";
    codigos.forEach(function (c) { const s = document.createElement("span"); s.textContent = c; caixa.appendChild(s); });
    $("caixa-codigos").hidden = false;
  }

  function desenharSeguranca() {
    $("estado-2fa").textContent = "";
    $("estado-2fa").appendChild(eu.doisFatores ? selo("Ativa", "ok") : selo("Desligada", "alerta"));
    $("ativar-2fa").hidden = eu.doisFatores;
    $("com-2fa").hidden = !eu.doisFatores;
  }
  desenharSeguranca();

  $("ativar-2fa").addEventListener("click", async function () {
    try {
      const r = await P.api("/api/conta/dois-fatores/iniciar", "POST");
      const qr = window.qrcode(0, "M");
      qr.addData(r.uri);
      qr.make();
      const img = document.createElement("img");
      img.src = qr.createDataURL(6, 2);
      img.alt = "QR code para cadastrar no aplicativo autenticador";
      $("qr").textContent = "";
      $("qr").appendChild(img);
      $("chave-2fa").textContent = r.chave;
      $("configurar-2fa").hidden = false;
      this.hidden = true;
      $("codigo-2fa").focus();
    } catch (err) { P.aviso($("aviso-2fa"), err.message, "erro"); }
  });

  $("form-2fa").addEventListener("submit", async function (e) {
    e.preventDefault();
    try {
      const r = await P.carregando(this.querySelector("button"), function () { return P.api("/api/conta/dois-fatores/ativar", "POST", { codigo: $("codigo-2fa").value }); });
      $("configurar-2fa").hidden = true;
      mostrarCodigos(r.codigosRecuperacao);
      P.aviso($("aviso-2fa"), "Verificação em duas etapas ativada. Guarde os códigos abaixo agora.", "ok");
      await recarregar();
    } catch (err) { P.aviso($("aviso-2fa"), err.message, "erro"); }
  });

  $("form-2fa-senha").addEventListener("submit", function (e) { e.preventDefault(); });
  $("novos-codigos").addEventListener("click", async function () {
    try {
      const r = await P.api("/api/conta/dois-fatores/novos-codigos", "POST", { senha: $("senha-2fa").value });
      mostrarCodigos(r.codigosRecuperacao);
      P.aviso($("aviso-2fa"), "Códigos novos gerados. Os antigos não valem mais.", "ok");
    } catch (err) { P.aviso($("aviso-2fa"), err.message, "erro"); }
  });
  $("desativar-2fa").addEventListener("click", async function () {
    try {
      await P.api("/api/conta/dois-fatores/desativar", "POST", { senha: $("senha-2fa").value });
      P.aviso($("aviso-2fa"), "Verificação em duas etapas desligada.", "ok");
      $("caixa-codigos").hidden = true;
      await recarregar();
    } catch (err) { P.aviso($("aviso-2fa"), err.message, "erro"); }
  });

  /* ---------- Meus dados (LGPD) ---------- */
  $("form-excluir").addEventListener("submit", async function (e) {
    e.preventDefault();
    try {
      await P.carregando(this.querySelector("button"), function () {
        return P.api("/api/conta/excluir", "POST", { senha: $("senha-excluir").value, confirmacao: $("confirma-excluir").value });
      });
      P.renovarSessao();
      location.href = "index.html";
    } catch (err) { P.aviso($("aviso-excluir"), err.message, "erro"); }
  });
})();
