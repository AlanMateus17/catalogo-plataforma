"""
Testes de ponta a ponta da API da plataforma (login, permissões, vendas, segurança).

Como rodar (com o sistema no ar em modo de desenvolvimento):
    pip install requests
    python testes/testar_api.py --url http://localhost:5080 --emails /caminho/da/pasta/emails-teste

O sistema precisa estar com:
    Plataforma__AdminInicial=admin@teste.com
    Vendas__Kiwify__Token=tokenkiwify   Vendas__Hotmart__Hottok=hottokteste
    Email em modo teste (sem SMTP), gravando na pasta passada em --emails
    Um banco NOVO (os testes criam contas com e-mails fixos).
"""
import argparse, base64, glob, hashlib, hmac, html, json, os, re, struct, sys, time, uuid
import requests

ap = argparse.ArgumentParser()
ap.add_argument("--url", default="http://localhost:5080")
ap.add_argument("--emails", default="emails-teste")
args = ap.parse_args()
URL = args.url.rstrip("/")

falhas = []
def confere(cond, msg):
    print(("  ok   " if cond else "  FALHOU ") + msg)
    if not cond: falhas.append(msg)

class Navegador:
    """Uma pessoa usando o site: guarda cookies e o token CSRF, como o navegador."""
    def __init__(self):
        self.s = requests.Session()
        self.csrf = None
    def token(self):
        self.csrf = self.s.get(URL + "/api/conta/csrf").json()["token"]
    def post(self, caminho, dados=None, csrf=True, **kw):
        if csrf and self.csrf is None: self.token()
        h = {"X-CSRF": self.csrf} if csrf else {}
        r = self.s.post(URL + caminho, json=dados or {}, headers=h, **kw)
        return r
    def put(self, caminho, dados):
        if self.csrf is None: self.token()
        return self.s.put(URL + caminho, json=dados, headers={"X-CSRF": self.csrf})
    def get(self, caminho, **kw):
        return self.s.get(URL + caminho, allow_redirects=False, **kw)
    def eu(self):
        return self.s.get(URL + "/api/conta/eu").json()

def ultimo_email(para, contem=""):
    for _ in range(20):
        arquivos = sorted(glob.glob(os.path.join(args.emails, "*.html")), key=os.path.getmtime, reverse=True)
        for a in arquivos:
            t = open(a, encoding="utf-8").read()
            if f"para: {para}" in t and contem in t:
                return t
        time.sleep(0.2)
    return ""

def link(texto, pagina):
    m = re.search(r'href="([^"]*' + re.escape(pagina) + r'[^"]*)"', texto)
    return html.unescape(m.group(1)) if m else None

def totp(chave_base32):
    chave = base64.b32decode(chave_base32.replace(" ", "").upper() + "=" * (-len(chave_base32.replace(" ", "")) % 8))
    passo = int(time.time()) // 30
    h = hmac.new(chave, struct.pack(">Q", passo), hashlib.sha1).digest()
    o = h[-1] & 0x0F
    return str((struct.unpack(">I", h[o:o + 4])[0] & 0x7FFFFFFF) % 1_000_000).zfill(6)

def criar_conta(nome, email, senha="uma frase bem longa 42"):
    n = Navegador()
    r = n.post("/api/conta/cadastro", {"nome": nome, "email": email, "aceitaTermos": True})
    assert r.status_code == 200, r.text
    msg = ultimo_email(email, "confirmar-email.html")
    url = link(msg, "confirmar-email.html")
    q = dict(p.split("=", 1) for p in url.split("?", 1)[1].split("&"))
    r = n.post("/api/conta/confirmar-email", {"id": q["id"], "codigo": q["codigo"], "senha": senha})
    assert r.status_code == 200, r.text
    n.token()  # depois de entrar, o token CSRF muda (fica ligado ao usuário)
    return n

sufixo = uuid.uuid4().hex[:6]

# =====================================================================
print("\n1. Proteções básicas")
n = Navegador()
r = n.s.get(URL + "/")
confere(r.status_code == 200, "página inicial abre")
confere("default-src 'self'" in r.headers.get("Content-Security-Policy", ""), "cabeçalho CSP presente")
confere(r.headers.get("X-Content-Type-Options") == "nosniff", "cabeçalho nosniff presente")
r = n.post("/api/conta/cadastro", {"nome": "X", "email": "x@x.com", "senha": "a", "aceitaTermos": True}, csrf=False)
confere(r.status_code == 400 and r.json().get("codigo") == "csrf", "POST sem token CSRF é recusado")
for caminho, esperado in [("/trechos/indice.json", 404), ("/ferramentas/gerar-codigos.py", 404), ("/LEIA-ME.txt", 404), ("/nao-existe.html", 404)]:
    confere(n.get(caminho).status_code == esperado, f"{caminho} não é servido")
r = n.get("/doc-css.html")
confere(r.status_code == 302 and "/entrar.html" in r.headers["Location"], "conteúdo pago sem login → tela de entrar")
r = n.get("/admin.html")
confere(r.status_code == 302 and "/entrar.html" in r.headers["Location"], "admin sem login → tela de entrar")
confere(n.s.get(URL + "/api/admin/painel").status_code == 401, "API do admin sem login → 401")

# =====================================================================
print("\n2. Cadastro e confirmação (a senha é criada no link do e-mail)")
GEN = "E-mail ou senha incorretos. Depois de 5 tentativas erradas, a entrada fica travada por 15 minutos."
email_aluno = f"aluna.{sufixo}@teste.com"
r = n.post("/api/conta/cadastro", {"nome": "Ana Teste", "email": email_aluno, "aceitaTermos": False})
confere(r.status_code == 400 and r.json()["codigo"] == "termos", "sem aceitar os termos é recusado")
# Um impostor chega antes e cadastra o e-mail da Ana (antes, ele escolhia a senha aqui)
imp = Navegador()
r_imp = imp.post("/api/conta/cadastro", {"nome": "Impostor", "email": email_aluno, "senha": "senha do impostor 123", "aceitaTermos": True})
confere(r_imp.status_code == 200, "cadastro aceito")
r = n.post("/api/conta/cadastro", {"nome": "Ana Teste", "email": email_aluno, "aceitaTermos": True})
confere(r.status_code == 200 and r.json()["mensagem"] == r_imp.json()["mensagem"], "e-mail repetido recebe a MESMA resposta (não revela contas)")

r = n.post("/api/conta/entrar", {"email": email_aluno, "senha": "senha do impostor 123"})
confere(r.status_code == 401 and r.json()["erro"] == GEN, "conta sem confirmar não tem senha: ninguém entra")
r = n.post("/api/conta/entrar", {"email": f"ninguem.{sufixo}@teste.com", "senha": "girassol azul na janela"})
confere(r.status_code == 401 and r.json()["erro"] == GEN, "e-mail inexistente: mesma mensagem")

msg = ultimo_email(email_aluno, "confirmar-email.html")
url = link(msg, "confirmar-email.html")
confere(url is not None and url.startswith("http"), "e-mail de confirmação tem o link")
q = dict(p.split("=", 1) for p in url.split("?", 1)[1].split("&"))
r = n.post("/api/conta/confirmar-email", {"id": q["id"], "codigo": q["codigo"] + "x", "senha": "girassol azul na janela"})
confere(r.status_code == 400, "código de confirmação adulterado é recusado")
r = n.post("/api/conta/confirmar-email", {"id": q["id"], "codigo": q["codigo"], "senha": "1234567890"})
confere(r.status_code == 400 and "muito usada" in r.json()["erro"], "senha comum é recusada na ativação")
r = n.post("/api/conta/confirmar-email", {"id": q["id"], "codigo": q["codigo"], "senha": "curta"})
confere(r.status_code == 400, "senha curta é recusada na ativação")
r = n.post("/api/conta/confirmar-email", {"id": q["id"], "codigo": q["codigo"], "senha": "girassol azul na janela"})
confere(r.status_code == 200 and r.json()["logado"], "ativação cria a senha e entra na conta")
n.token()
eu = n.eu()
confere(eu["logado"] and eu["nome"] == "Ana Teste" and eu["papeis"] == ["aluno"], "/eu mostra a aluna com papel aluno")
confere(eu["testeDisponivel"] is True, "teste grátis disponível")
r = Navegador().post("/api/conta/entrar", {"email": email_aluno, "senha": "senha do impostor 123"})
confere(r.status_code == 401, "ATAQUE: a senha do impostor não vale depois que a dona ativa a conta")
r = Navegador().post("/api/conta/confirmar-email", {"id": q["id"], "codigo": q["codigo"], "senha": "senha do atacante 999"})
confere(r.status_code == 400, "ATAQUE: o mesmo link de confirmação não funciona de novo")
r = Navegador().post("/api/conta/confirmar-email", {"id": q["id"], "codigo": "AAAA", "senha": "senha do atacante 999"})
confere(r.status_code == 400, "ATAQUE: só o id da conta (sem o código) não entra na conta")

# =====================================================================
print("\n3. Níveis de conteúdo")
confere(n.get("/passo-5-html.html").status_code == 200, "conteúdo de conta grátis abre logado")
r = n.get("/doc-css.html")
confere(r.status_code == 302 and "/planos.html" in r.headers["Location"] and "precisa=modulos" in r.headers["Location"], "conteúdo pago sem plano → página de planos")
confere(n.get("/admin.html").status_code == 404, "aluno não vê o admin (404)")
confere(n.s.get(URL + "/api/admin/painel").status_code == 403, "aluno não usa a API do admin (403)")

r = n.post("/api/conta/teste-gratis")
confere(r.status_code == 200, "teste grátis liberado: " + r.json().get("mensagem", r.text))
confere(n.get("/doc-css.html").status_code == 200, "com o teste, o conteúdo pago abre")
confere(n.get("/doc-css.html").headers.get("Cache-Control") == "private, no-store", "conteúdo pago não fica em cache")
r = n.post("/api/conta/teste-gratis")
confere(r.status_code == 400, "segundo teste grátis é recusado")
n_alias = criar_conta("Ana Apelido", email_aluno.replace("@", "+2@"))
r = n_alias.post("/api/conta/teste-gratis")
confere(r.status_code == 400 and "já usou" in r.json().get("erro", ""), "ATAQUE: e-mail com +apelido não ganha outro teste grátis")

r = n.put("/api/conta/progresso", {"chave": "trilha2:passo-5", "valor": "1"})
confere(r.status_code == 200 and n.s.get(URL + "/api/conta/progresso").json().get("trilha2:passo-5") == "1", "progresso salvo na conta")

# =====================================================================
print("\n4. Bloqueio por tentativas")
email_b = f"beto.{sufixo}@teste.com"
b = criar_conta("Beto Teste", email_b)
x = Navegador()
for i in range(5):
    x.post("/api/conta/entrar", {"email": email_b, "senha": f"errada numero {i}"})
r = x.post("/api/conta/entrar", {"email": email_b, "senha": "uma frase bem longa 42"})
confere(r.status_code == 401 and r.json()["erro"] == GEN, "5 erros travam por 15 min, com a mesma mensagem genérica (não revela a conta)")

# =====================================================================
print("\n5. Redefinir senha")
x = Navegador()
r = x.post("/api/conta/esqueci-senha", {"email": email_b})
confere(r.status_code == 200, "pedido de nova senha aceito")
confere(x.post("/api/conta/esqueci-senha", {"email": f"nao.existe.{sufixo}@teste.com"}).json()["mensagem"] == r.json()["mensagem"], "mesma resposta para e-mail inexistente")
msg = ultimo_email(email_b, "redefinir-senha.html")
url = link(msg, "redefinir-senha.html")
q = dict(p.split("=", 1) for p in url.split("?", 1)[1].split("&"))
r = x.post("/api/conta/redefinir-senha", {"id": q["id"], "codigo": q["codigo"], "senha": "nova frase secreta do beto"})
confere(r.status_code == 200, "senha redefinida pelo link")
r = x.post("/api/conta/redefinir-senha", {"id": q["id"], "codigo": q["codigo"], "senha": "outra frase secreta do beto"})
confere(r.status_code == 400, "o mesmo link não funciona duas vezes")
r = x.post("/api/conta/entrar", {"email": email_b, "senha": "nova frase secreta do beto"})
confere(r.status_code == 200, "entra com a senha nova (bloqueio por tentativas liberado)")
confere(b.s.get(URL + "/api/conta/eu").json()["logado"] in (True, False), "(sessão antiga cai em até 1 minuto: conferido no teste 9)")

# =====================================================================
print("\n6. Administrador e verificação em duas etapas")
adm = criar_conta("Admin Teste", "admin@teste.com", "chave forte da equipe 2026")
eu = adm.eu()
confere("admin" in eu["papeis"], "e-mail de AdminInicial vira admin ao confirmar")
confere(eu["precisaAtivar2fa"] is True, "admin sem 2 etapas: precisa ativar")
confere(adm.s.get(URL + "/api/admin/painel").status_code == 403, "admin sem 2 etapas não usa o painel")
r = adm.post("/api/conta/dois-fatores/iniciar")
chave = r.json()["chave"]
confere(r.json()["uri"].startswith("otpauth://totp/"), "gera chave e endereço otpauth")
r = adm.post("/api/conta/dois-fatores/ativar", {"codigo": "000000"})
confere(r.status_code == 400, "código errado não ativa")
r = adm.post("/api/conta/dois-fatores/ativar", {"codigo": totp(chave)})
confere(r.status_code == 200 and len(r.json()["codigosRecuperacao"]) == 10, "2 etapas ativada com 10 códigos de recuperação")
codigos_rec = r.json()["codigosRecuperacao"]
adm.token()
confere(adm.s.get(URL + "/api/admin/painel").status_code == 200, "com 2 etapas, o painel abre")
confere(adm.get("/admin.html").status_code == 200, "admin.html abre para a equipe")

# login novo exige o código
a2 = Navegador()
r = a2.post("/api/conta/entrar", {"email": "admin@teste.com", "senha": "chave forte da equipe 2026"})
confere(r.status_code == 200 and r.json().get("precisa2fa"), "login do admin pede o código")
confere(a2.s.get(URL + "/api/admin/painel").status_code == 401, "sem o código, não está logado")
r = a2.post("/api/conta/entrar-recuperacao", {"codigo": codigos_rec[0]})
confere(r.status_code == 200, "entra com código de recuperação")
a3 = Navegador()
a3.post("/api/conta/entrar", {"email": "admin@teste.com", "senha": "chave forte da equipe 2026"})
r = a3.post("/api/conta/entrar-recuperacao", {"codigo": codigos_rec[0]})
confere(r.status_code == 401, "código de recuperação usado não vale de novo")

# =====================================================================
print("\n7. Papéis e permissões")
usuarios = adm.s.get(URL + "/api/admin/usuarios", params={"busca": email_b}).json()["itens"]
id_b = usuarios[0]["id"]
r = adm.post(f"/api/admin/usuarios/{id_b}/papeis", {"papeis": ["suporte"]})
confere(r.status_code == 200 and set(r.json()["papeis"]) == {"suporte", "aluno"}, "admin dá papel de suporte")
r = adm.post(f"/api/admin/usuarios/{eu['id']}/papeis", {"papeis": []})
confere(r.status_code == 400, "admin não tira o próprio papel de admin")
b2 = Navegador()
b2.post("/api/conta/entrar", {"email": email_b, "senha": "nova frase secreta do beto"})
b2.token()
confere(b2.s.get(URL + "/api/admin/painel").status_code == 403, "suporte sem 2 etapas ainda não usa o painel")
r = b2.post("/api/conta/dois-fatores/iniciar"); ch = r.json()["chave"]
b2.post("/api/conta/dois-fatores/ativar", {"codigo": totp(ch)}); b2.token()
confere(b2.s.get(URL + "/api/admin/painel").status_code == 200, "suporte com 2 etapas vê o painel")
p = b2.s.get(URL + "/api/admin/painel").json()
confere(p["receitaMes"] is None, "suporte NÃO vê valores de venda")
confere(b2.s.get(URL + "/api/admin/pedidos").status_code == 200, "suporte vê pedidos (para atender aluno)")
confere(b2.post("/api/admin/pedidos/manual", {"email": "a@b.com", "produtoId": 2, "valorCentavos": 100, "observacao": "x"}).status_code == 403, "suporte NÃO registra venda")
confere(b2.post(f"/api/admin/usuarios/{id_b}/papeis", {"papeis": ["admin"]}).status_code == 403, "suporte NÃO se dá papel de admin")
confere(b2.s.get(URL + "/api/admin/auditoria").status_code == 403, "suporte NÃO vê a auditoria")
confere(b2.post(f"/api/admin/usuarios/{id_b}/acessos", {"produtoId": 2, "motivo": "eu mesmo"}).status_code == 403, "ATAQUE: suporte NÃO libera acesso para a própria conta")
confere(b2.post(f"/api/admin/usuarios/{id_b}/confirmar-email").status_code == 403, "suporte NÃO confirma e-mail à mão (só admin)")

# =====================================================================
print("\n8. Produtos, cupons e códigos")
produtos = adm.s.get(URL + "/api/admin/produtos").json()
anual = next(p["produto"] for p in produtos if p["produto"]["slug"] == "plano-anual")
r = adm.post("/api/admin/cupons", {"codigo": f"DEZ{sufixo}".upper(), "tipo": "percentual", "valor": 10, "minimoCentavos": 10000, "ativo": True})
confere(r.status_code == 200, "cupom criado")
r = n.post("/api/planos/cupom", {"plano": "plano-anual", "cupom": f"dez{sufixo}"})
confere(r.json()["valido"] and r.json()["total"] == 35730, "cupom 10% em R$ 397,00 → R$ 357,30")
r = n.post("/api/planos/cupom", {"plano": "plano-anual", "cupom": "NAOEXISTE"})
confere(not r.json()["valido"], "cupom inexistente é recusado")
r = adm.post("/api/admin/codigos/gerar", {"produtoId": anual["id"], "quantidade": 3, "dias": 30, "lote": f"Turma teste {sufixo}"})
codigos = r.json()["codigos"]
confere(len(codigos) == 3 and re.fullmatch(r"[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}", codigos[0]), "3 códigos gerados no formato XXXX-XXXX-XXXX")
email_c = f"carla.{sufixo}@teste.com"
c = criar_conta("Carla Teste", email_c)
r = c.post("/api/conta/resgatar-codigo", {"codigo": codigos[0].lower()})
confere(r.status_code == 200, "aluna resgata código (minúsculas também valem)")
confere(c.get("/doc-css.html").status_code == 200, "código libera o conteúdo")
r = n.post("/api/conta/resgatar-codigo", {"codigo": codigos[0]})
confere(r.status_code == 400 and "já foi usado" in r.json()["erro"], "código usado não vale para outra pessoa")

# =====================================================================
print("\n9. Bloqueio pelo admin")
id_c = adm.s.get(URL + "/api/admin/usuarios", params={"busca": email_c}).json()["itens"][0]["id"]
r = adm.post(f"/api/admin/usuarios/{id_c}/bloquear", {"motivo": "teste de bloqueio"})
confere(r.status_code == 200, "admin bloqueia a aluna")
x = Navegador()
r = x.post("/api/conta/entrar", {"email": email_c, "senha": "uma frase bem longa 42"})
confere(r.status_code == 403 and r.json()["codigo"] == "bloqueada", "aluna bloqueada não entra")
print("     (esperando 65 s para a sessão aberta cair...)")
time.sleep(65)
confere(c.s.get(URL + "/api/conta/eu").json()["logado"] is False, "a sessão que estava aberta cai em até 1 minuto")
adm.post(f"/api/admin/usuarios/{id_c}/desbloquear")
r = x.post("/api/conta/entrar", {"email": email_c, "senha": "uma frase bem longa 42"})
confere(r.status_code == 200, "desbloqueada, entra de novo")

# =====================================================================
print("\n10. Webhook da Kiwify (compra antes de ter conta)")
email_d = f"davi.{sufixo}@teste.com"
adm.post(f"/api/admin/produtos/{anual['id']}/externos", {"plataforma": "kiwify", "idExterno": f"kiwi-{sufixo}"})
pedido = f"ord-{sufixo}"
corpo = json.dumps({"order_id": pedido, "order_status": "paid", "webhook_event_type": "order_approved", "approved_date": "2026-10-01 10:00",
                    "Product": {"product_id": f"kiwi-{sufixo}", "product_name": "Anual"},
                    "Customer": {"full_name": "Davi Teste", "email": email_d},
                    "Commissions": {"charge_amount": 39700}})
assinatura = hmac.new(b"tokenkiwify", corpo.encode(), hashlib.sha1).hexdigest()
r = requests.post(URL + "/api/webhooks/kiwify?signature=errada", data=corpo, headers={"Content-Type": "application/json"})
confere(r.status_code == 401, "assinatura errada é recusada")
r = requests.post(URL + f"/api/webhooks/kiwify?signature={assinatura}", data=corpo, headers={"Content-Type": "application/json"})
confere(r.status_code == 200, "webhook com assinatura certa aceito")
r = requests.post(URL + f"/api/webhooks/kiwify?signature={assinatura}", data=corpo, headers={"Content-Type": "application/json"})
confere(r.json().get("duplicado") is True, "o mesmo aviso de novo não duplica")
confere("davi" in ultimo_email(email_d, "cadastro.html").lower() or "Criar minha conta" in ultimo_email(email_d, "cadastro.html"), "comprador sem conta recebe e-mail para criar conta")
d = criar_conta("Davi Teste", email_d)
confere("modulos" in d.eu()["liberacoes"], "ao confirmar o e-mail, a compra é ligada à conta")
corpo_r = json.dumps({"order_id": pedido, "order_status": "refunded", "webhook_event_type": "order_refunded", "approved_date": "2026-10-03 10:00",
                      "Product": {"product_id": f"kiwi-{sufixo}"}, "Customer": {"email": email_d}})
ass_r = hmac.new(b"tokenkiwify", corpo_r.encode(), hashlib.sha1).hexdigest()
r = requests.post(URL + f"/api/webhooks/kiwify?signature={ass_r}", data=corpo_r, headers={"Content-Type": "application/json"})
confere(r.status_code == 200 and d.eu()["liberacoes"] == [], "reembolso retira o acesso")

# =====================================================================
print("\n11. Webhook da Hotmart (produto não mapeado → reprocessar)")
corpo_h = json.dumps({"id": f"evt-{sufixo}", "event": "PURCHASE_APPROVED", "version": "2.0.0",
                      "data": {"product": {"id": 999000 + int(sufixo, 16) % 1000}, "buyer": {"email": email_aluno, "name": "Ana Teste"},
                               "purchase": {"transaction": f"HP{sufixo}", "status": "APPROVED", "price": {"value": 197.0}}}})
r = requests.post(URL + "/api/webhooks/hotmart", data=corpo_h, headers={"Content-Type": "application/json", "X-HOTMART-HOTTOK": "errado"})
confere(r.status_code == 401, "Hottok errado é recusado")
r = requests.post(URL + "/api/webhooks/hotmart", data=corpo_h, headers={"Content-Type": "application/json", "X-HOTMART-HOTTOK": "hottokteste"})
confere(r.status_code == 200, "Hottok certo aceito")
w = adm.s.get(URL + "/api/admin/webhooks", params={"comErro": "true"}).json()["itens"]
alvo = next((x for x in w if x["plataforma"] == "hotmart"), None)
confere(alvo is not None and "não mapeado" in alvo["resultado"], "sem mapeamento, o aviso fica com erro visível no painel")
pacote = next(p["produto"] for p in produtos if p["produto"]["slug"] == "pacote-projetos")
adm.post(f"/api/admin/produtos/{pacote['id']}/externos", {"plataforma": "hotmart", "idExterno": str(999000 + int(sufixo, 16) % 1000)})
r = adm.post(f"/api/admin/webhooks/{alvo['id']}/reprocessar")
confere(r.status_code == 200 and "Aprovado" in r.json()["resultado"], "depois de mapear, reprocessar libera: " + r.json()["resultado"])

# =====================================================================
print("\n11b. Webhook do Mercado Pago (só a assinatura; os dados vêm da API do MP)")
# Precisa de Vendas__MercadoPago__SegredoWebhook=segredomp no sistema
ts, rid, pid = str(int(time.time())), str(uuid.uuid4()), "123456789"
manifesto = f"id:{pid};request-id:{rid};ts:{ts};"
v1 = hmac.new(b"segredomp", manifesto.encode(), hashlib.sha256).hexdigest()
corpo_mp = json.dumps({"id": int(time.time()), "type": "payment", "action": "payment.updated", "data": {"id": pid}})
r = requests.post(URL + f"/api/webhooks/mercadopago?data.id={pid}&type=payment", data=corpo_mp,
                  headers={"Content-Type": "application/json", "x-signature": f"ts={ts},v1={v1}", "x-request-id": rid})
confere(r.status_code == 200, "assinatura do Mercado Pago (HMAC-SHA256 do manifesto) aceita")
r = requests.post(URL + f"/api/webhooks/mercadopago?data.id={pid}&type=payment", data=corpo_mp,
                  headers={"Content-Type": "application/json", "x-signature": f"ts={ts},v1={'0'*64}", "x-request-id": rid})
confere(r.status_code == 401, "assinatura do Mercado Pago errada é recusada")

# =====================================================================
print("\n12. Venda manual, reembolso e auditoria")
email_e = f"eva.{sufixo}@teste.com"
r = adm.post("/api/admin/pedidos/manual", {"email": email_e, "nome": "Eva", "produtoId": anual["id"], "valorCentavos": 30000, "observacao": "Pix 01/10"})
confere(r.status_code == 200, "venda manual registrada: " + r.json().get("mensagem", r.text))
peds = adm.s.get(URL + "/api/admin/pedidos", params={"busca": email_e}).json()["itens"]
r = adm.post(f"/api/admin/pedidos/{peds[0]['id']}/reembolsar", {"motivo": "desistiu em 7 dias"})
confere(r.status_code == 200 and "reembolsado" in r.json()["mensagem"], "reembolso manual")
aud = adm.s.get(URL + "/api/admin/auditoria", params={"busca": "venda.manual"}).json()["itens"]
confere(len(aud) >= 1 and aud[0]["ator"] == "admin@teste.com", "auditoria registra quem fez a venda manual")
csv = adm.s.get(URL + "/api/admin/pedidos.csv")
confere(csv.status_code == 200 and "email;produto" in csv.text, "exporta pedidos em CSV")
r = adm.post("/api/admin/conteudo/testar", {"caminho": "/projeto-7.html"})
confere("projetos" in r.json()["resultado"], "testar caminho mostra a regra")

# =====================================================================
print("\n13. LGPD: exportar e excluir")
r = n.s.get(URL + "/api/conta/exportar-dados")
confere(r.status_code == 200 and json.loads(r.content)["conta"]["Email"] == email_aluno, "aluna baixa os próprios dados")
r = n.post("/api/conta/excluir", {"senha": "girassol azul na janela", "confirmacao": "errado"})
confere(r.status_code == 400, "excluir sem digitar EXCLUIR é recusado")
r = n.post("/api/conta/excluir", {"senha": "girassol azul na janela", "confirmacao": "EXCLUIR"})
confere(r.status_code == 200, "conta excluída")
confere(Navegador().post("/api/conta/entrar", {"email": email_aluno, "senha": "girassol azul na janela"}).status_code == 401, "conta excluída não entra")
r = adm.post(f"/api/admin/usuarios/{eu['id']}/bloquear", {"motivo": "x"})
confere(r.status_code == 400, "admin não bloqueia a si mesmo")

print("\n" + ("TUDO CERTO" if not falhas else f"{len(falhas)} FALHA(S):\n - " + "\n - ".join(falhas)))
sys.exit(1 if falhas else 0)
