# Componentes e molde das páginas da trilha "Passo a passo"
import html as H

PASSOS = [
    # (numero, arquivo, titulo curto, titulo longo, duracao, resumo)
    (1, "passo-1-preparar.html", "Preparar o computador", "Preparar o computador", "1 a 2 aulas",
     "Criar a conta no GitHub, instalar VS Code, Git e GitHub CLI e entrar na sua conta pelo terminal."),
    (2, "passo-2-terminal.html", "Terminal sem medo", "Terminal sem medo", "1 aula",
     "Ler o prompt, andar entre pastas e criar arquivos com pwd, ls, cd, mkdir e touch."),
    (3, "passo-3-projeto.html", "Criar o projeto", "Criar o projeto pelo terminal", "1 aula",
     "Criar a pasta e os três arquivos, colar a estrutura e ver o site no Live Server."),
    (4, "passo-4-github.html", "Guardar no GitHub", "Guardar o projeto no GitHub", "1 aula",
     "Criar o repositório com git init e commit e enviar com gh repo create. Daqui em diante, o seu trabalho nunca fica só num computador."),
    (5, "passo-5-html.html", "HTML", "Montar o conteúdo com HTML", "2 aulas",
     "Planejar a página e montar as seções com os códigos prontos, trocando o conteúdo pelo seu."),
    (6, "passo-6-css.html", "CSS", "Dar a cara do site com CSS", "2 aulas",
     "Suas cores e sua fonte, regras suas e o ajuste para o celular com @media."),
    (7, "passo-7-js.html", "JavaScript", "Dar vida com JavaScript", "1 a 2 aulas",
     "Colocar 2 ou 3 interações e aprender a ler os erros do Console."),
    (8, "passo-8-verificar.html", "Verificar", "Verificar se está tudo funcionando", "1 aula",
     "Usar as ferramentas do navegador (F12), o Verificador do projeto, o validador e o Lighthouse."),
    (9, "passo-9-pages.html", "GitHub Pages", "Publicar no GitHub Pages pelo terminal", "1 aula",
     "Ativar o GitHub Pages com um comando, abrir o seu site na internet e caprichar no link compartilhado."),
    (10, "passo-10-atualizar.html", "Atualizar", "Atualizar o site publicado", "1 aula",
     "O ciclo de todo dia (pull, status, add, commit, push), voltar a uma versão antiga e continuar em outro computador."),
]
TEMPO_TOTAL = "de 12 a 14 aulas de 50 minutos"


def esc(t):
    return H.escape(t, quote=True)


def terminal(linhas, titulo="Git Bash", nota=None):
    """linhas: lista de strings. '# texto' vira comentário (não é copiado)."""
    partes = []
    for linha in linhas:
        if linha.startswith("#"):
            partes.append(f'<span class="t-linha t-coment">{esc(linha)}</span>')
        else:
            partes.append(f'<span class="t-linha"><span class="t-prompt" aria-hidden="true">$ </span><span class="t-cmd">{esc(linha)}</span></span>')
    corpo = "".join(partes)   # cada linha já é um bloco; sem \n extra entre elas
    extra = f'<p class="terminal-nota">{nota}</p>' if nota else ""
    return f'''<div class="terminal">
  <div class="terminal-topo"><span>{esc(titulo)}</span><button type="button" class="terminal-copiar">Copiar</button></div>
  <pre><code>{corpo}</code></pre>
</div>{extra}'''


def saida(texto, titulo="O que deve aparecer"):
    return f'''<div class="saida">
  <p class="saida-titulo">{esc(titulo)}</p>
  <pre><code>{esc(texto)}</code></pre>
</div>'''


def codigo(texto, linguagem="html", titulo=None):
    t = f'<p class="saida-titulo">{esc(titulo)}</p>' if titulo else ""
    return f'<div class="bloco-exemplo">{t}<pre class="bloco-codigo"><code>{esc(texto)}</code></pre></div>'


def confira(pergunta, resposta):
    return f'''<div class="confira">
  <p><strong>Confira:</strong> {pergunta}</p>
  <details><summary>Ver a resposta</summary><p>{resposta}</p></details>
</div>'''


def erro(titulo, texto):
    return f'''<div class="erro-comum">
  <p class="erro-comum-titulo"><strong>Erro comum:</strong> {titulo}</p>
  {texto}
</div>'''


def tenta(texto):
    return f'''<div class="voce-tenta">
  <p class="voce-tenta-titulo">Você tenta</p>
  {texto}
</div>'''


def pronto(numero, itens):
    lis = "\n".join(
        f'    <li><label><input type="checkbox" data-progresso="passo-{numero}-{i}"> {item}</label></li>'
        for i, item in enumerate(itens, 1)
    )
    return f'''<section class="pronto-quando" aria-labelledby="pronto-titulo">
  <h2 id="pronto-titulo">Pronto quando</h2>
  <p>Marque cada item só depois de conferir. As marcações ficam salvas neste navegador e aparecem na página inicial da trilha.</p>
  <ul data-total="{len(itens)}">
{lis}
  </ul>
</section>'''


def teoria(*links):
    """Caixa com links para a teoria na Documentação."""
    itens = "".join(f'<li><a href="{h}">{t}</a></li>' for h, t in links)
    return f'''<aside class="doc-nota trilha-teoria">
  <p class="doc-nota-titulo">Para entender a fundo</p>
  <p>Este passo usa o que está explicado na Documentação. Leia antes, ou volte lá quando uma palavra não fizer sentido:</p>
  <ul>{itens}</ul>
</aside>'''


def usar_ia(pode, nao_pode):
    return f'''<aside class="trilha-ia">
  <p class="trilha-ia-titulo">E a IA, neste passo?</p>
  <p><strong>Pode:</strong> {pode}</p>
  <p><strong>Não:</strong> {nao_pode}</p>
  <p>Por quê, e prompts prontos para usar do jeito certo: <a href="ia.html">Usar IA do jeito certo</a>.</p>
</aside>'''


def guardar(numero, ident, mensagem):
    """Fim dos passos 5 a 8: registrar e enviar para o GitHub."""
    return secao(ident, f"{numero} Guardar no GitHub", f'''
<p>Terminou este passo? Registre a versão e envie. Assim, se o computador do laboratório for formatado, o seu trabalho estará salvo no GitHub.</p>
''' + terminal(["git status", "git add .", f'git commit -m "{mensagem}"', "git push"],
    nota="Troque a mensagem se quiser: ela deve dizer o que mudou. O site ainda não está no ar; publicar é o Passo 9.") + '''
<p>Outro computador, ou começou a aula agora? Antes de editar, traga o que já está no GitHub: veja o <a href="passo-10-atualizar.html#outro-pc">Passo 10.3</a>.</p>
''')


def secao(ident, titulo, conteudo):
    return f'''<section class="trilha-secao" id="{ident}">
  <h2>{titulo}</h2>
  {conteudo}
</section>'''


def cabecalho_html(atual):
    itens = [
        ("index.html", "Catálogo", "catalogo"),
        ("passo-a-passo.html", "Passo a passo", "trilha"),
        ("documentacao.html", "Documentação", "documentacao"),
        ("projetos.html", "Projetos", "projetos"),
        ("codigos.html", "Códigos", "codigos"),
        ("verificador.html", "Verificador", "verificador"),
        ("sobre.html", "Sobre", "sobre"),
    ]
    lis = []
    for href, nome, chave in itens:
        marca = ' aria-current="page"' if chave == atual else ""
        lis.append(f'          <li><a href="{href}"{marca}>{nome}</a></li>')
    lis = "\n".join(lis)
    return f'''  <a class="pular-para-conteudo" href="#conteudo">Pular para o conteúdo</a>
  <div class="barra-progresso" id="barra-progresso" aria-hidden="true"></div>

  <header class="cabecalho" id="cabecalho">
    <div class="container cabecalho-conteudo">
      <a href="index.html" class="logo" aria-label="Catálogo do HTML, página inicial">
        <svg class="logo-icone" viewBox="0 0 40 40" width="40" height="40" aria-hidden="true">
          <rect width="40" height="40" rx="9" fill="currentColor" />
          <path d="M16 12 L8 20 L16 28" class="logo-traco" />
          <path d="M24 12 L32 20 L24 28" class="logo-traco" />
        </svg>
        <span class="logo-texto">Catálogo<br>do HTML</span>
      </a>

      <button class="btn-menu" id="btn-menu" type="button" aria-expanded="false" aria-controls="menu-principal">
        <span class="btn-menu-linhas" aria-hidden="true"></span>
        <span class="visualmente-escondido">Abrir menu</span>
      </button>

      <nav class="menu" aria-label="Menu principal">
        <ul class="menu-lista" id="menu-principal">
{lis}
        </ul>
      </nav>

      <button class="btn-tema" id="btn-tema" type="button" aria-pressed="false">
        <span class="btn-tema-icone" aria-hidden="true">◐</span>
        <span class="btn-tema-texto">Tema escuro</span>
      </button>
    </div>
  </header>
'''


HEAD_COMUM = '''  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <link rel="icon" type="image/png" href="img/favicon.png">
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Atkinson+Hyperlegible:ital,wght@0,400;0,700;1,400&family=Fraunces:opsz,wght@9..144,600;9..144,800&family=JetBrains+Mono:wght@400;600&display=swap">
  <link rel="stylesheet" href="css/estilo.css">
  <script src="js/script.js" defer></script>
  <script src="js/plataforma.js" defer></script>'''

RODAPE = '''  <div class="toast" id="toast" role="status" aria-live="polite"></div>
  <a href="#inicio" class="voltar-topo" id="voltar-topo" aria-label="Voltar ao topo">↑</a>

  <footer class="rodape">
    <p class="rodape-direitos"><small>&copy; <span id="ano-atual">2026</span> Prof. Alan Mateus. <a href="passo-a-passo.html">Passo a passo</a>, <a href="documentacao.html">Documentação</a>, <a href="dicionario.html">Dicionário</a>, <a href="ia.html">Usar IA do jeito certo</a>, <a href="codigos.html">Códigos</a> e <a href="verificador.html">Verificador</a>.</small></p>
    <p class="rodape-legal"><small><a href="planos.html">Planos</a> · <a href="compra-segura.html">Compra segura</a> · <a href="termos.html">Termos de uso</a> · <a href="privacidade.html">Privacidade</a></small></p>
  </footer>
</body>
</html>
'''


TOTAIS = {}


def mapa_trilha(atual):
    itens = []
    for n, arq, curto, *_ in PASSOS:
        marca = ' aria-current="step"' if n == atual else ""
        itens.append(f'<li><a href="{arq}" data-passo="{n}" data-total="{TOTAIS.get(n, 0)}"{marca}><span class="mapa-num">{n}</span><span class="mapa-nome">{curto}</span></a></li>')
    return '<nav class="trilha-mapa" aria-label="Passos da trilha"><ol>' + "".join(itens) + "</ol></nav>"


def pagina_passo(numero, lead, conteudo, pronto_itens, com_paineis=False):
    n, arq, curto, longo, duracao, resumo = PASSOS[numero - 1]
    anterior = PASSOS[numero - 2] if numero > 1 else None
    proximo = PASSOS[numero] if numero < len(PASSOS) else None
    nav_ant = f'<a class="passo-ant" href="{anterior[1]}"><span>Passo anterior</span>{anterior[0]}. {anterior[2]}</a>' if anterior else '<a class="passo-ant" href="passo-a-passo.html"><span>Voltar</span>Início da trilha</a>'
    nav_prox = f'<a class="passo-prox" href="{proximo[1]}"><span>Próximo passo</span>{proximo[0]}. {proximo[2]}</a>' if proximo else '<a class="passo-prox" href="passo-a-passo.html"><span>Fim da trilha</span>Ver o seu progresso</a>'
    scripts_extra = '''
  <script src="js/codigos-dados.js" defer></script>
  <script src="js/dicionario-dados.js" defer></script>
  <script src="js/analisador.js" defer></script>
  <script src="js/entenda.js" defer></script>
  <script src="js/painel-codigos.js" defer></script>''' if com_paineis else ""
    return f'''<!DOCTYPE html>
<!--
  TRILHA "PASSO A PASSO" — Passo {n}: {longo}
  Página de conteúdo. Os comandos têm botão de copiar e as caixas
  "Pronto quando" guardam o progresso no navegador (js/trilha.js).
-->
<html lang="pt-BR">
<head>
{HEAD_COMUM}
  <script src="js/trilha.js" defer></script>{scripts_extra}
  <title>Passo {n}: {esc(longo)} — Catálogo do HTML</title>
  <meta name="description" content="{esc(resumo)}">
</head>
<body class="pagina-trilha">
{cabecalho_html("trilha")}
  <main id="conteudo">
    <section class="heroi heroi-curto" id="inicio">
      <div class="container heroi-conteudo">
        <nav aria-label="Você está em" class="migalhas">
          <ol>
            <li><a href="index.html">Início</a></li>
            <li><a href="passo-a-passo.html">Passo a passo</a></li>
            <li aria-current="page">Passo {n}</li>
          </ol>
        </nav>
        <p class="heroi-antetitulo">Passo {n} de {len(PASSOS)}. Tempo: {esc(duracao)}</p>
        <h1 class="heroi-titulo">{esc(longo)}</h1>
        <p class="heroi-texto">{lead}</p>
      </div>
    </section>

    <div class="container trilha-corpo">
      {mapa_trilha(n)}
{conteudo}
      {pronto(n, pronto_itens)}
      <nav class="passo-nav" aria-label="Navegação entre passos">
        {nav_ant}
        {nav_prox}
      </nav>
    </div>
  </main>

{RODAPE}'''
