/*
  acesso.js — FERRAMENTA DO SITE (os alunos não precisam copiar este arquivo)
  As telas de entrar, criar conta, confirmar e-mail e trocar a senha.
  Qual tela é: <body data-tela="entrar|cadastro|confirmar|esqueci|redefinir">.
  Quem confere senha, bloqueio e tudo mais é o servidor; aqui só mostramos o resultado.
*/
"use strict";

(function () {
  const P = window.Plataforma;
  const tela = document.body.dataset.tela;
  const $ = function (id) { return document.getElementById(id); };

  // Mostrar/ocultar senha (botões com data-mostrar="idDoCampo")
  document.querySelectorAll("[data-mostrar]").forEach(function (b) {
    b.addEventListener("click", function () {
      const campo = $(b.dataset.mostrar);
      const mostrar = campo.type === "password";
      campo.type = mostrar ? "text" : "password";
      b.textContent = mostrar ? "Ocultar" : "Mostrar";
      b.setAttribute("aria-pressed", String(mostrar));
    });
  });

  // Medidor simples: comprimento e variedade (o servidor é quem decide)
  function medirSenha(campo, barra, dica) {
    if (!campo || !barra) return;
    campo.addEventListener("input", function () {
      const s = campo.value;
      let pontos = Math.min(s.length, 20) / 20 * 70;
      if (/[a-z]/.test(s) && /[A-Z]/.test(s)) pontos += 10;
      if (/\d/.test(s)) pontos += 10;
      if (/[^A-Za-z0-9]/.test(s)) pontos += 10;
      if (s.length < 10) pontos = Math.min(pontos, 30);
      const cor = pontos < 40 ? "var(--cor-erro)" : pontos < 70 ? "var(--cor-alerta)" : "var(--cor-verde)";
      barra.style.width = pontos + "%";
      barra.style.backgroundColor = cor;
      if (dica) dica.textContent = s.length < 10 ? "Faltam " + (10 - s.length) + " caracteres." :
        pontos < 70 ? "Boa. Uma frase com 3 ou 4 palavras é ainda mais forte." : "Senha forte.";
    });
  }

  // Já está logado? As telas de entrar e cadastro mandam direto para a conta
  if (tela === "entrar" || tela === "cadastro") {
    P.eu().then(function (d) { if (d.logado) location.replace(P.destinoSeguro("conta.html")); });
  }

  /* ---------------- ENTRAR ---------------- */
  if (tela === "entrar") {
    const form = $("form-entrar"), form2 = $("form-2fa"), aviso = $("aviso");
    let lembrar = false, usandoRecuperacao = false;

    if (P.param("confirmado")) P.aviso(aviso, "E-mail confirmado. Agora é só entrar.", "ok");
    if (P.param("sessao") === "encerrada") P.aviso(aviso, "Sua sessão terminou. Entre de novo.", "info");
    if (P.param("volta") && /\.html/.test(P.param("volta"))) {
      P.aviso(aviso, "Entre na sua conta para abrir esta página. Não tem conta? Criar é grátis.", "info");
    }

    form.addEventListener("submit", async function (e) {
      e.preventDefault();
      P.aviso(aviso, "");
      $("reenviar").hidden = true;
      lembrar = $("lembrar").checked;
      const botao = form.querySelector("button[type=submit]");
      try {
        const r = await P.carregando(botao, function () {
          return P.api("/api/conta/entrar", "POST", { email: $("email").value, senha: $("senha").value, lembrar: lembrar });
        });
        if (r.precisa2fa) {
          form.hidden = true;
          form2.hidden = false;
          $("codigo").focus();
          return;
        }
        P.renovarSessao();
        location.href = P.destinoSeguro("conta.html");
      } catch (err) {
        P.aviso(aviso, err.message, "erro");
        if (err.codigo === "email-nao-confirmado") $("reenviar").hidden = false;
      }
    });

    $("reenviar").addEventListener("click", async function () {
      try {
        const r = await P.api("/api/conta/reenviar-confirmacao", "POST", { email: $("email").value });
        P.aviso(aviso, r.mensagem, "ok");
      } catch (err) { P.aviso(aviso, err.message, "erro"); }
    });

    $("usar-recuperacao").addEventListener("click", function () {
      usandoRecuperacao = !usandoRecuperacao;
      $("rotulo-codigo").textContent = usandoRecuperacao ? "Código de recuperação" : "Código do aplicativo (6 números)";
      $("codigo").inputMode = usandoRecuperacao ? "text" : "numeric";
      $("codigo").maxLength = usandoRecuperacao ? 20 : 7;
      $("codigo").classList.toggle("campo-codigo", !usandoRecuperacao);
      this.textContent = usandoRecuperacao ? "Usar o código do aplicativo" : "Perdi o celular: usar código de recuperação";
      $("codigo").value = "";
      $("codigo").focus();
    });

    form2.addEventListener("submit", async function (e) {
      e.preventDefault();
      P.aviso(aviso, "");
      const botao = form2.querySelector("button[type=submit]");
      try {
        const r = await P.carregando(botao, function () {
          return P.api(usandoRecuperacao ? "/api/conta/entrar-recuperacao" : "/api/conta/entrar-2fa", "POST",
            { codigo: $("codigo").value, lembrar: lembrar, lembrarDispositivo: $("lembrar-dispositivo").checked });
        });
        P.renovarSessao();
        if (r.aviso) alert(r.aviso);
        location.href = P.destinoSeguro("conta.html");
      } catch (err) {
        P.aviso(aviso, err.message, "erro");
        if (err.codigo === "expirou") { form2.hidden = true; form.hidden = false; }
      }
    });
  }

  /* ---------------- CRIAR CONTA ---------------- */
  if (tela === "cadastro") {
    const form = $("form-criar-conta"), aviso = $("aviso");
    if (P.param("email")) $("email").value = P.param("email");
    form.addEventListener("submit", async function (e) {
      e.preventDefault();
      P.aviso(aviso, "");
      const botao = form.querySelector("button[type=submit]");
      try {
        const r = await P.carregando(botao, function () {
          return P.api("/api/conta/cadastro", "POST", {
            nome: $("nome").value, email: $("email").value, aceitaTermos: $("termos").checked,
          });
        });
        form.hidden = true;
        $("enviado-para").textContent = $("email").value;
        $("enviado").hidden = false;
        P.aviso(aviso, r.mensagem, "ok");
      } catch (err) { P.aviso(aviso, err.message, "erro"); }
    });
  }

  /* ---------------- CONFIRMAR E-MAIL (e criar a senha) ---------------- */
  if (tela === "confirmar") {
    const form = $("form-confirmar"), aviso = $("aviso");
    const id = P.param("id"), codigo = P.param("codigo");
    // Remove o código da barra de endereço (não fica no histórico nem vaza em print)
    history.replaceState(null, "", location.pathname);
    medirSenha($("senha"), $("forca"), $("dica-senha"));
    if (!id || !codigo) {
      P.aviso(aviso, "Link incompleto. Abra de novo o link do e-mail ou crie a conta outra vez para receber um novo link.", "erro");
      form.hidden = true; $("pedir-outro").hidden = false;
    }
    form.addEventListener("submit", async function (e) {
      e.preventDefault();
      if ($("senha").value !== $("senha2").value) { P.aviso(aviso, "As duas senhas não são iguais.", "erro"); return; }
      const botao = form.querySelector("button[type=submit]");
      try {
        const r = await P.carregando(botao, function () {
          return P.api("/api/conta/confirmar-email", "POST", { id: id, codigo: codigo, senha: $("senha").value });
        });
        P.renovarSessao();
        let texto = "Conta ativada! Seu e-mail está confirmado e a senha foi criada.";
        if (r.comprasLiberadas > 0) texto += " Encontramos sua compra e ela já está liberada.";
        P.aviso(aviso, texto, "ok");
        form.hidden = true;
        $("seguir").hidden = false;
        $("seguir").href = r.logado ? "conta.html" : "entrar.html?confirmado=1";
      } catch (err) {
        P.aviso(aviso, err.message, "erro");
        if (err.codigo === "link" || err.codigo === "ja-confirmado") { form.hidden = true; $("pedir-outro").hidden = false; }
      }
    });
  }

  /* ---------------- ESQUECI A SENHA ---------------- */
  if (tela === "esqueci") {
    const form = $("form-esqueci"), aviso = $("aviso");
    form.addEventListener("submit", async function (e) {
      e.preventDefault();
      const botao = form.querySelector("button[type=submit]");
      try {
        const r = await P.carregando(botao, function () { return P.api("/api/conta/esqueci-senha", "POST", { email: $("email").value }); });
        P.aviso(aviso, r.mensagem, "ok");
        form.hidden = true;
      } catch (err) { P.aviso(aviso, err.message, "erro"); }
    });
  }

  /* ---------------- NOVA SENHA ---------------- */
  if (tela === "redefinir") {
    const form = $("form-redefinir"), aviso = $("aviso");
    const id = P.param("id"), codigo = P.param("codigo");
    history.replaceState(null, "", location.pathname);
    medirSenha($("senha"), $("forca"), $("dica-senha"));
    if (!id || !codigo) { P.aviso(aviso, "Link incompleto. Peça um novo em \"Esqueci minha senha\".", "erro"); form.hidden = true; }
    form.addEventListener("submit", async function (e) {
      e.preventDefault();
      if ($("senha").value !== $("senha2").value) { P.aviso(aviso, "As duas senhas não são iguais.", "erro"); return; }
      const botao = form.querySelector("button[type=submit]");
      try {
        const r = await P.carregando(botao, function () {
          return P.api("/api/conta/redefinir-senha", "POST", { id: id, codigo: codigo, senha: $("senha").value });
        });
        P.aviso(aviso, r.mensagem, "ok");
        form.hidden = true;
        $("seguir").hidden = false;
      } catch (err) { P.aviso(aviso, err.message, "erro"); }
    });
  }
})();
