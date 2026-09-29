/*
  planos.js — FERRAMENTA DO SITE (os alunos não precisam copiar este arquivo)
  Monta os cartões de preço a partir da API (os preços ficam no painel, não no HTML)
  e leva ao pagamento: Kiwify, Hotmart ou o checkout do Mercado Pago do próprio site.
*/
"use strict";

(async function () {
  const P = window.Plataforma;
  const $ = function (id) { return document.getElementById(id); };
  const grade = $("planos");
  const NOMES_LIB = { modulos: "os módulos", projetos: "os projetos" };

  const bloqueado = P.param("bloqueado");
  if (bloqueado) {
    const precisa = P.param("precisa");
    P.aviso($("aviso-bloqueado"), "Esta página faz parte da formação" + (precisa && NOMES_LIB[precisa] ? " (" + NOMES_LIB[precisa] + ")" : "") +
      ". Escolha um plano abaixo ou comece pelo teste grátis de 7 dias.", "info");
  }

  let dados;
  try { dados = await P.api("/api/planos"); }
  catch (e) {
    grade.innerHTML = "";
    const p = document.createElement("p");
    p.textContent = "Os planos não carregaram agora. Recarregue a página em instantes.";
    grade.appendChild(p);
    return;
  }
  const eu = await P.eu();

  function el(tag, classe, texto) {
    const e = document.createElement(tag);
    if (classe) e.className = classe;
    if (texto != null) e.textContent = texto;
    return e;
  }

  function periodo(p) {
    if (p.tipo === "assinatura") return p.dias && p.dias <= 31 ? "/mês" : "";
    if (!p.dias) return " acesso vitalício";
    if (p.dias >= 365) return " por " + Math.round(p.dias / 365) + (p.dias >= 730 ? " anos" : " ano");
    return " por " + p.dias + " dias";
  }

  grade.textContent = "";
  dados.planos.forEach(function (p) {
    const card = el("article", "plano" + (p.destaque ? " plano-destaque" : ""));
    if (p.destaque) card.appendChild(el("span", "plano-faixa", "Mais escolhido"));
    card.appendChild(el("h3", null, p.nome));
    if (p.precoDe && p.precoDe > p.preco) card.appendChild(el("p", "plano-de", "de " + P.dinheiro(p.precoDe)));
    const preco = el("p", "plano-preco", P.dinheiro(p.preco));
    preco.appendChild(el("small", null, periodo(p)));
    card.appendChild(preco);
    if (p.parcelas > 1) card.appendChild(el("p", "ajuda", "ou até " + p.parcelas + "x de " + P.dinheiro(Math.ceil(p.preco / p.parcelas)) + " (com juros da plataforma)"));
    card.appendChild(el("p", null, p.descricao));
    if (p.beneficios.length) {
      const ul = el("ul");
      p.beneficios.forEach(function (b) { ul.appendChild(el("li", null, b)); });
      card.appendChild(ul);
    }

    const acoes = el("div", "plano-acoes");
    const aviso = el("p", "aviso");
    aviso.hidden = true;
    aviso.tabIndex = -1;

    if (p.mercadoPago) {
      const cupomLinha = el("div", "plano-cupom");
      const cupom = el("input");
      cupom.placeholder = "Cupom (opcional)";
      cupom.setAttribute("aria-label", "Cupom de desconto para " + p.nome);
      cupom.autocapitalize = "characters";
      const aplicar = el("button", "botao botao-contorno botao-pequeno", "Aplicar");
      aplicar.type = "button";
      aplicar.addEventListener("click", async function () {
        try {
          const r = await P.api("/api/planos/cupom", "POST", { plano: p.slug, cupom: cupom.value });
          P.aviso(aviso, r.valido ? r.mensagem + " Total: " + P.dinheiro(r.total) : r.mensagem, r.valido ? "ok" : "erro");
        } catch (err) { P.aviso(aviso, err.message, "erro"); }
      });
      cupomLinha.append(cupom, aplicar);
      const pagar = el("button", "botao botao-primario", "Comprar com Pix ou cartão");
      pagar.type = "button";
      pagar.addEventListener("click", async function () {
        if (!eu.logado) { location.href = "entrar.html?volta=" + encodeURIComponent("/planos.html"); return; }
        try {
          const r = await P.carregando(pagar, function () { return P.api("/api/planos/checkout", "POST", { plano: p.slug, cupom: cupom.value }); });
          location.href = r.url;
        } catch (err) { P.aviso(aviso, err.message, "erro"); }
      });
      acoes.append(cupomLinha, pagar);
    }
    [["kiwify", "Comprar pela Kiwify"], ["hotmart", "Comprar pela Hotmart"]].forEach(function (par) {
      if (!p[par[0]]) return;
      const a = el("a", "botao" + (p.mercadoPago ? " botao-contorno" : " botao-primario"), par[1]);
      a.href = p[par[0]];
      a.rel = "noopener";
      acoes.appendChild(a);
    });
    if (!acoes.children.length) acoes.appendChild(el("p", "ajuda", "Venda direta: fale com o professor pelo contato abaixo."));
    acoes.appendChild(aviso);
    card.appendChild(acoes);
    grade.appendChild(card);
  });

  // Chamada para o teste grátis
  const cta = $("cta-teste");
  if (cta) {
    if (eu.logado && !eu.testeDisponivel) cta.hidden = true;
    else cta.href = eu.logado ? "conta.html#teste" : "cadastro.html";
  }
})();
