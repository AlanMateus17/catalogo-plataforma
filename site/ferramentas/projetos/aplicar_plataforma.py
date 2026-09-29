# Aplica nas páginas existentes (idempotente): link Projetos no menu, plataforma.js e rodapé legal.
import glob, os, re
SITE = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
LEGAL = '    <p class="rodape-legal"><small><a href="planos.html">Planos</a> · <a href="compra-segura.html">Compra segura</a> · <a href="termos.html">Termos de uso</a> · <a href="privacidade.html">Privacidade</a></small></p>\n'
mudou = []
for arq in sorted(glob.glob(SITE + "/*.html")):
    s = open(arq, encoding="utf-8").read(); o = s
    if 'id="menu-principal"' in s and '<li><a href="projetos.html"' not in s:
        s = re.sub(r'(\s*<li><a href="documentacao.html"[^>]*>Documentação</a></li>)', r'\1\n          <li><a href="projetos.html">Projetos</a></li>', s, count=1)
    if 'script src="js/plataforma.js"' not in s and '\n  <script src="js/script.js" defer></script>' in s:
        s = s.replace('\n  <script src="js/script.js" defer></script>', '\n  <script src="js/script.js" defer></script>\n  <script src="js/plataforma.js" defer></script>', 1)
    if 'rodape-legal' not in s and '<footer class="rodape"' in s:
        i = s.index('<footer class="rodape"'); j = s.index('</footer>', i)
        s = s[:j] + LEGAL + "  " + s[j:]
    if s != o:
        open(arq, "w", encoding="utf-8").write(s); mudou.append(os.path.basename(arq))
print("alteradas:", len(mudou), mudou)
