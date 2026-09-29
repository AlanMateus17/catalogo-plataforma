"""
Testa as telas no navegador (Playwright): páginas sem erro no Console, sem rolagem lateral no celular,
e o caminho completo de um aluno e de um administrador.

    pip install playwright requests && playwright install chromium
    python testes/testar_telas.py --url http://localhost:5080 --emails /pasta/emails-teste --fotos /pasta/para/capturas
Precisa de banco novo e do sistema com Plataforma__AdminInicial=admin@teste.com.
"""
import argparse, base64, glob, hashlib, hmac, html, os, re, struct, sys, time, uuid
from playwright.sync_api import sync_playwright

ap = argparse.ArgumentParser()
ap.add_argument("--url", default="http://localhost:5080")
ap.add_argument("--emails", default="emails-teste")
ap.add_argument("--fotos", default="capturas")
args = ap.parse_args()
URL = args.url.rstrip("/")
os.makedirs(args.fotos, exist_ok=True)
falhas = []

def confere(cond, msg):
    print(("  ok   " if cond else "  FALHOU ") + msg)
    if not cond: falhas.append(msg)

def ultimo_link(para, pagina):
    for _ in range(30):
        for a in sorted(glob.glob(os.path.join(args.emails, "*.html")), key=os.path.getmtime, reverse=True):
            t = open(a, encoding="utf-8").read()
            if f"para: {para}" in t and pagina in t:
                return html.unescape(re.search(r'href="([^"]*' + re.escape(pagina) + r'[^"]*)"', t).group(1))
        time.sleep(0.2)
    return None

def totp(chave):
    chave = chave.replace(" ", "").upper()
    k = base64.b32decode(chave + "=" * (-len(chave) % 8))
    h = hmac.new(k, struct.pack(">Q", int(time.time()) // 30), hashlib.sha1).digest()
    o = h[-1] & 15
    return str((struct.unpack(">I", h[o:o + 4])[0] & 0x7FFFFFFF) % 1000000).zfill(6)

erros_console = []
def vigiar(pagina):
    # Recusas esperadas da API (400, 401, 403, 409) aparecem no Console, mas são testadas de propósito.
    # Fontes do Google bloqueadas na rede do teste não contam como erro do site
    pagina.on("console", lambda m: m.type == "error" and "ERR_TUNNEL" not in m.text and "fonts.g" not in m.text and not re.search(r"status of 40[0139]", m.text) and erros_console.append(f"{pagina.url}: {m.text}"))
    pagina.on("pageerror", lambda e: erros_console.append(f"{pagina.url}: {e}"))

def sem_rolagem_lateral(p):
    return p.evaluate("document.documentElement.scrollWidth <= window.innerWidth + 1")

sufixo = uuid.uuid4().hex[:6]
with sync_playwright() as pw:
    nav = pw.chromium.launch()

    print("\n1. Páginas públicas no computador e no celular")
    for largura, nome in [(1280, "pc"), (375, "cel")]:
        ctx = nav.new_context(bypass_csp=True, viewport={"width": largura, "height": 900})
        p = ctx.new_page(); vigiar(p)
        for pag in ["index.html", "projetos.html", "projeto-1.html", "planos.html", "entrar.html", "cadastro.html", "esqueci-senha.html",
                    "compra-segura.html", "termos.html", "privacidade.html", "passo-a-passo.html", "documentacao.html"]:
            p.goto(f"{URL}/{pag}"); p.wait_for_load_state("networkidle")
            confere(sem_rolagem_lateral(p), f"{pag} ({largura}px) sem rolagem lateral")
            if pag in ("planos.html", "projetos.html", "entrar.html", "projeto-1.html"):
                p.screenshot(path=f"{args.fotos}/{nome}-{pag.replace('.html', '')}.png", full_page=pag != "projeto-1.html")
        menu = p.locator("#menu-principal .menu-conta a")
        if largura == 1280:
            confere(menu.count() == 1 and menu.first.inner_text() == "Entrar", "menu mostra Entrar para visitante")
            confere(p.locator("#menu-principal a[href='projetos.html']").count() == 1, "menu tem o link Projetos")
        ctx.close()

    print("\n2. Aluno: cadastro, confirmação, teste grátis, projeto pago e progresso")
    ctx = nav.new_context(bypass_csp=True, viewport={"width": 1280, "height": 900})
    p = ctx.new_page(); vigiar(p)
    email = f"aluno.{sufixo}@teste.com"
    p.goto(f"{URL}/projeto-2.html")
    confere("/entrar.html" in p.url, "projeto pago sem login leva para entrar")
    confere(p.locator("#aviso").is_visible(), "tela de entrar explica por que pediu login")
    p.goto(f"{URL}/cadastro.html")
    p.fill("#nome", "Aluno Tela"); p.fill("#email", email)
    p.click("button[type=submit]")
    p.wait_for_selector("#aviso.aviso-erro")
    confere(p.locator("#aviso").is_visible() and "aceite" in p.locator("#aviso").inner_text(), "sem aceitar termos aparece o erro")
    p.check("#termos"); p.click("button[type=submit]")
    p.wait_for_selector("#enviado", state="visible")
    confere(True, "cadastro mostra 'Confira seu e-mail'")
    p.goto(ultimo_link(email, "confirmar-email.html"))
    p.wait_for_selector("#form-confirmar")
    confere("?" not in p.url, "o código some da barra de endereço")
    p.fill("#senha", "estudo todo dia 2027"); p.fill("#senha2", "outra coisa")
    p.click("#form-confirmar button[type=submit]"); p.wait_for_selector("#aviso.aviso-erro")
    confere("iguais" in p.locator("#aviso").inner_text(), "ativação confere se as duas senhas são iguais")
    p.fill("#senha2", "estudo todo dia 2027"); p.click("#form-confirmar button[type=submit]")
    p.wait_for_selector("#seguir", state="visible")
    confere(True, "ativação cria a senha e mostra Continuar")
    p.click("#seguir"); p.wait_for_selector("#ola")
    p.wait_for_function("document.getElementById('ola').textContent.includes('Aluno')")
    confere(p.locator("#caixa-teste").is_visible(), "conta oferece o teste grátis")
    p.goto(f"{URL}/projeto-2.html")
    confere("/planos.html" in p.url, "sem plano, projeto pago leva para planos")
    p.wait_for_selector(".plano")
    confere(p.locator("#aviso-bloqueado").is_visible(), "planos explica que a página é da formação")
    confere(p.locator(".plano").count() >= 3, "planos mostra os cartões de preço")
    p.goto(f"{URL}/conta.html"); p.wait_for_selector("#btn-teste", state="visible")
    p.click("#btn-teste"); p.wait_for_selector("#aviso-teste.aviso-ok")
    confere("Teste liberado" in p.locator("#aviso-teste").inner_text(), "teste grátis liberado pela tela")
    p.goto(f"{URL}/projeto-2.html")
    confere(p.url.endswith("/projeto-2.html"), "com o teste, o projeto 2 abre")
    p.locator("[data-progresso='passo-p2-1']").check()
    time.sleep(1)
    ctx2 = nav.new_context(bypass_csp=True, viewport={"width": 1280, "height": 900})
    q = ctx2.new_page(); vigiar(q)
    q.goto(f"{URL}/entrar.html"); q.fill("#email", email); q.fill("#senha", "estudo todo dia 2027"); q.click("button[type=submit]")
    q.wait_for_url("**/conta.html")
    q.goto(f"{URL}/projeto-2.html"); time.sleep(1.5)
    confere(q.locator("[data-progresso='passo-p2-1']").is_checked(), "progresso marcado num navegador aparece no outro (salvo na conta)")
    q.goto(f"{URL}/projetos.html"); time.sleep(1.5)
    confere(q.locator("[data-passo='p2'] .feitos").inner_text() == "1", "mapa dos projetos mostra o progresso")
    ctx2.close()
    p.goto(f"{URL}/projeto-1.html")
    p.locator(".dicas summary").first.click()
    confere(p.locator(".dicas details").first.get_attribute("open") is not None, "dica em camadas abre")
    p.screenshot(path=f"{args.fotos}/pc-projeto-1-dica.png")

    print("\n3. Administrador: 2 etapas pela tela e painel")
    a = nav.new_context(bypass_csp=True, viewport={"width": 1280, "height": 900})
    p = a.new_page(); vigiar(p)
    p.goto(f"{URL}/cadastro.html")
    p.fill("#nome", "Admin Tela"); p.fill("#email", "admin@teste.com"); p.check("#termos")
    p.click("button[type=submit]"); p.wait_for_selector("#enviado", state="visible")
    p.goto(ultimo_link("admin@teste.com", "confirmar-email.html")); p.wait_for_selector("#form-confirmar")
    p.fill("#senha", "chave forte da equipe 2026"); p.fill("#senha2", "chave forte da equipe 2026")
    p.click("#form-confirmar button[type=submit]"); p.wait_for_selector("#seguir", state="visible")
    p.goto(f"{URL}/admin.html"); p.wait_for_selector("#area .aviso-erro")
    confere("duas etapas" in p.locator("#area").inner_text(), "painel pede 2 etapas antes de abrir")
    p.goto(f"{URL}/conta.html#seguranca"); p.wait_for_selector("#ativar-2fa", state="visible")
    p.click("#ativar-2fa"); p.wait_for_selector("#qr img")
    confere(p.locator("#qr img").count() == 1, "QR code aparece")
    p.screenshot(path=f"{args.fotos}/pc-conta-2fa.png", full_page=True)
    p.fill("#codigo-2fa", totp(p.locator("#chave-2fa").inner_text()))
    p.click("#form-2fa button[type=submit]"); p.wait_for_selector("#caixa-codigos", state="visible")
    confere(p.locator("#codigos-recuperacao span").count() == 10, "mostra 10 códigos de recuperação")
    p.goto(f"{URL}/index.html"); p.wait_for_selector(".menu-painel a")
    confere(p.locator(".menu-painel a").inner_text() == "Painel", "menu mostra Painel para a equipe")
    p.goto(f"{URL}/admin.html"); p.wait_for_selector(".admin-numero")
    confere(p.locator(".admin-abas button").count() == 9, "admin vê as 9 abas")
    p.screenshot(path=f"{args.fotos}/pc-admin-painel.png", full_page=True)
    for aba in ["usuarios", "pedidos", "produtos", "cupons", "codigos", "conteudo", "webhooks", "auditoria"]:
        p.click(f"#aba-{aba}"); p.wait_for_function("!document.getElementById('area').textContent.startsWith('Carregando')")
        confere(p.locator("#area .aviso-erro").count() == 0, f"aba {aba} abre sem erro")
        if aba in ("usuarios", "produtos", "conteudo"): p.screenshot(path=f"{args.fotos}/pc-admin-{aba}.png", full_page=True)
    p.click("#aba-usuarios"); p.wait_for_selector("tr.clicavel")
    p.locator("tr.clicavel", has_text=email).click(); p.wait_for_selector("#dialogo[open]")
    p.screenshot(path=f"{args.fotos}/pc-admin-usuario.png")
    confere("Aluno Tela" in p.locator("#dialogo-titulo").inner_text(), "detalhe do usuário abre")
    p.click("#dialogo-fechar")
    p.click("#aba-produtos"); p.wait_for_selector("#area table")
    p.locator("button", has_text="Editar").first.click(); p.wait_for_selector("#dialogo[open]")
    p.screenshot(path=f"{args.fotos}/pc-admin-produto.png")
    p.click("#dialogo-fechar")
    p.set_viewport_size({"width": 375, "height": 800}); p.click("#aba-painel") if p.locator("#aba-painel").is_visible() else None
    time.sleep(1)
    confere(sem_rolagem_lateral(p), "painel sem rolagem lateral no celular")
    p.screenshot(path=f"{args.fotos}/cel-admin.png", full_page=True)
    a.close()
    nav.close()

print("\nErros no Console:" if erros_console else "\nNenhum erro no Console.")
for e in erros_console[:30]: print("  ", e)
if erros_console: falhas.append(f"{len(erros_console)} erro(s) no Console")
print("\n" + ("TUDO CERTO" if not falhas else f"{len(falhas)} FALHA(S):\n - " + "\n - ".join(falhas)))
sys.exit(1 if falhas else 0)
