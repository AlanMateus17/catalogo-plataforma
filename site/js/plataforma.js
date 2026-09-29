/*
  plataforma.js — FERRAMENTA DO SITE (os alunos não precisam copiar este arquivo)

  Liga as páginas ao sistema (a API em /api). Faz quatro coisas:
  1. Plataforma.api(): chama a API com o token de segurança (CSRF) e trata os erros.
  2. Mostra "Entrar" ou "Minha conta" no menu, conforme a pessoa esteja logada.
  3. Sincroniza o progresso da trilha e dos projetos com a conta (vale em qualquer computador).
  4. Utilidades das telas de conta: mensagens, botão "carregando", formatar dinheiro e data.

  Se o site estiver aberto sem o sistema (ex.: GitHub Pages ou dois cliques no arquivo),
  a API não responde e tudo continua funcionando como antes, só sem login.
*/
"use strict";

window.Plataforma = (function () {
  let csrf = null;
  let euCache = null;

  /* ---------- 1. Chamar a API ---------- */
  async function pegarCsrf() {
    const r = await fetch("/api/conta/csrf", { credentials: "same-origin" });
    csrf = (await r.json()).token;
    return csrf;
  }

  // metodo: "GET", "POST", "PUT", "DELETE". Devolve o JSON. Em erro, lança Error com a mensagem da API.
  async function api(caminho, metodo, dados, tentouDeNovo) {
    metodo = metodo || "GET";
    const opcoes = { method: metodo, credentials: "same-origin", headers: {} };
    if (metodo !== "GET") {
      if (!csrf) await pegarCsrf();
      opcoes.headers["X-CSRF"] = csrf;
      opcoes.headers["Content-Type"] = "application/json";
      opcoes.body = JSON.stringify(dados || {});
    }
    let resposta;
    try {
      resposta = await fetch(caminho, opcoes);
    } catch (e) {
      throw new Error("Sem conexão com o servidor. Confira a internet e tente de novo.");
    }
    let corpo = null;
    const tipo = resposta.headers.get("Content-Type") || "";
    if (tipo.indexOf("json") >= 0) corpo = await resposta.json();

    // O token de segurança muda quando a pessoa entra ou sai: pega um novo e tenta uma vez
    if (resposta.status === 400 && corpo && corpo.codigo === "csrf" && !tentouDeNovo) {
      await pegarCsrf();
      return api(caminho, metodo, dados, true);
    }
    if (!resposta.ok) {
      const erro = new Error((corpo && corpo.erro) || mensagemPadrao(resposta.status));
      erro.status = resposta.status;
      erro.codigo = corpo && corpo.codigo;
      erro.corpo = corpo;
      throw erro;
    }
    return corpo;
  }

  function mensagemPadrao(status) {
    if (status === 401) return "Sua sessão terminou. Entre de novo.";
    if (status === 403) return "Você não tem permissão para isso.";
    if (status === 404) return "Não encontrado.";
    if (status === 429) return "Muitas tentativas. Espere um minuto.";
    return "Algo deu errado no servidor. Tente de novo em instantes.";
  }

  // Depois de entrar ou sair, o token de segurança antigo não vale mais
  function renovarSessao() {
    csrf = null;
    euCache = null;
  }

  /* ---------- 2. Quem está logado ---------- */
  function eu() {
    if (!euCache) {
      euCache = fetch("/api/conta/eu", { credentials: "same-origin" })
        .then(function (r) { return r.ok ? r.json() : { logado: false, semSistema: true }; })
        .catch(function () { return { logado: false, semSistema: true }; });
    }
    return euCache;
  }

  function montarMenu(dados) {
    const lista = document.getElementById("menu-principal");
    if (!lista || dados.semSistema) return;
    let item = lista.querySelector(".menu-conta");
    if (!item) {
      item = document.createElement("li");
      item.className = "menu-conta";
      lista.appendChild(item);
    }
    item.textContent = "";
    const link = document.createElement("a");
    if (dados.logado) {
      link.href = "conta.html";
      link.textContent = "Minha conta";
      if (location.pathname.endsWith("/conta.html")) link.setAttribute("aria-current", "page");
      if (dados.equipe) {
        const painel = document.createElement("li");
        painel.className = "menu-conta menu-painel";
        const lp = document.createElement("a");
        lp.href = "admin.html";
        lp.textContent = "Painel";
        painel.appendChild(lp);
        lista.insertBefore(painel, item);
      }
    } else {
      link.href = "entrar.html?volta=" + encodeURIComponent(location.pathname + location.search);
      link.textContent = "Entrar";
    }
    item.appendChild(link);
  }

  /* ---------- 3. Progresso na conta ---------- */
  const PREFIXOS = ["trilha2:", "projeto:"];

  async function sincronizarProgresso() {
    const dados = await eu();
    if (!dados.logado) return;
    let doServidor;
    try { doServidor = await api("/api/conta/progresso"); } catch (e) { return; }
    let locais = {};
    try {
      Object.keys(localStorage).forEach(function (k) {
        if (PREFIXOS.some(function (p) { return k.indexOf(p) === 0; })) locais[k] = localStorage.getItem(k);
      });
    } catch (e) { locais = {}; }
    // O que está só neste navegador sobe para a conta; o que está só na conta desce para cá
    Object.keys(locais).forEach(function (k) {
      if (!(k in doServidor)) salvarProgresso(k, locais[k]);
    });
    Object.keys(doServidor).forEach(function (k) {
      try { localStorage.setItem(k, doServidor[k]); } catch (e) {}
    });
    document.dispatchEvent(new CustomEvent("plataforma:progresso"));
  }

  // chaveCompleta já com o prefixo (ex.: "trilha2:passo-5-1"). valor null apaga.
  function salvarProgresso(chaveCompleta, valor) {
    eu().then(function (d) {
      if (!d.logado) return;
      api("/api/conta/progresso", "PUT", { chave: chaveCompleta, valor: valor }).catch(function () {});
    });
  }

  /* ---------- 4. Utilidades das telas ---------- */
  function dinheiro(centavos) {
    return (centavos / 100).toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
  }
  function data(iso, comHora) {
    if (!iso) return "—";
    const d = new Date(iso);
    const o = { day: "2-digit", month: "2-digit", year: "numeric" };
    if (comHora) { o.hour = "2-digit"; o.minute = "2-digit"; }
    return d.toLocaleString("pt-BR", o);
  }
  // Mostra uma mensagem numa caixa .aviso (tipo: "ok", "erro", "info")
  function aviso(elemento, texto, tipo) {
    if (!elemento) return;
    elemento.hidden = !texto;
    elemento.textContent = texto || "";
    elemento.className = "aviso" + (tipo ? " aviso-" + tipo : "");
    if (texto && tipo === "erro") elemento.focus && elemento.focus();
  }
  // Desliga o botão enquanto espera a resposta (evita clique duplo)
  async function carregando(botao, trabalho) {
    const texto = botao.textContent;
    botao.disabled = true;
    botao.setAttribute("aria-busy", "true");
    botao.textContent = "Aguarde...";
    try { return await trabalho(); }
    finally {
      botao.disabled = false;
      botao.removeAttribute("aria-busy");
      botao.textContent = texto;
    }
  }
  // Só aceita voltar para uma página DESTE site (evita mandar a pessoa para um site falso)
  function destinoSeguro(padrao) {
    const volta = new URLSearchParams(location.search).get("volta");
    // Só caminhos simples deste site (ex.: /projeto-2.html). Nada de //outro-site, \\, tab ou esquemas.
    if (volta && /^\/[A-Za-z0-9._\/-]*(\?[A-Za-z0-9=&._-]*)?(#[A-Za-z0-9_-]*)?$/.test(volta) && volta.charAt(1) !== "/") {
      try { if (new URL(volta, location.origin).origin === location.origin) return volta; } catch (e) { /* inválido */ }
    }
    return padrao || "conta.html";
  }
  function param(nome) {
    return new URLSearchParams(location.search).get(nome);
  }

  document.addEventListener("DOMContentLoaded", function () {
    eu().then(montarMenu);
    sincronizarProgresso();
  });

  return { api: api, eu: eu, renovarSessao: renovarSessao, salvarProgresso: salvarProgresso,
           dinheiro: dinheiro, data: data, aviso: aviso, carregando: carregando, destinoSeguro: destinoSeguro, param: param };
})();
