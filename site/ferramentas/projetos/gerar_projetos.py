# Gera projetos.html (mapa dos 12) e projeto-1.html a projeto-4.html (os que já podem ser feitos).
import os
from trilha_base import cabecalho_html, HEAD_COMUM, RODAPE, esc, erro, tenta, usar_ia


def teoria(*links):
    itens = "".join(f'<li><a href="{h}">{t}</a></li>' for h, t in links)
    return f'''<aside class="doc-nota trilha-teoria">
  <p class="doc-nota-titulo">O que estudar antes (ou durante)</p>
  <p>Tudo o que este projeto usa está explicado na Documentação:</p>
  <ul>{itens}</ul>
</aside>'''

SAIDA = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

# (n, titulo, inspirado, etapa, modulos, tecnologias, duracao, formato, disponivel, libera_apos)
MAPA = [
    (1, "Caixa Rápido", "AM Kaixara (PDV)", 1, "1", "JavaScript puro", "2 a 3 semanas", "Individual", True, None),
    (2, "Linha do Tempo", "AM Vynla (histórias e linha do tempo)", 2, "2", "HTML, CSS, GitHub Pages", "2 a 3 semanas", "Individual", True, None),
    (3, "Portfólio pessoal", "o seu site pessoal", 2, "2 e 3", "HTML, CSS, JavaScript", "3 a 4 semanas", "Individual", True, None),
    (4, "Quadro Kanban", "AM Taskoro (gestão de projetos)", 2, "2", "JavaScript, DOM, localStorage", "3 a 4 semanas", "Individual", True, None),
    (5, "Finanças em dupla", "AM Rendara (finanças e rebalanceamento)", 3, "3", "JavaScript, CSV, Git em equipe", "4 semanas", "Dupla", False, "o módulo 3 (Git em equipe)"),
    (6, "Agenda com API", "AM Horaria (agendamentos)", 3, "4", "fetch, HTTP, API falsa (json-server)", "3 a 4 semanas", "Individual", False, "o módulo 4 (Como a web funciona)"),
    (7, "Loja virtual", "AM Vendra (e-commerce)", 4, "5 e 6", "TypeScript, React, Next.js, Tailwind, Zustand", "5 a 6 semanas", "Individual", False, "os módulos 5 e 6 (TypeScript, React e Next.js)"),
    (8, "Ordens de serviço", "AM Consertta (assistência técnica)", 5, "7 e 8", "C#, POO, LINQ, JSON", "4 a 5 semanas", "Individual", False, "os módulos 7 e 8 (C# e POO, SOLID)"),
    (9, "Banco de dados do PDV", "AM Kaixara (PDV)", 6, "10", "SQL, PostgreSQL, Docker Compose", "4 semanas", "Individual", False, "o módulo 10 (SQL e PostgreSQL)"),
    (10, "API da clínica veterinária", "AM Petara (veterinária)", 7, "9, 10 e 12", "ASP.NET Core, EF Core, JWT, PostgreSQL", "6 a 8 semanas", "Individual", False, "os módulos 9, 10 e 12 (API, banco e segurança)"),
    (11, "Reservas do condomínio com TDD", "AM Predara (condomínios)", 8, "14", "xUnit, testes de integração, Playwright", "4 a 5 semanas", "Individual", False, "o módulo 14 (Testes automatizados)"),
    (12, "Sistema completo (1 de 3)", "AM Kaixara, AM Rotara ou AM Mentor", 9, "todos", "A stack inteira, Docker, GitHub Actions, deploy", "8 a 12 semanas", "Individual ou dupla", False, "o módulo 17 (Projeto final)"),
]
RESUMOS = {
    1: "O caixa de uma padaria: soma a venda, aplica o desconto certo e diz quais cédulas e moedas devolver de troco.",
    2: "A história de uma cidade, família ou escola numa linha do tempo que funciona no celular. Só HTML e CSS.",
    3: "O seu portfólio: projetos montados pelo JavaScript a partir de uma lista, com filtros, janela de detalhes e tema.",
    4: "Um quadro A fazer / Fazendo / Feito com arrastar e soltar, limite de trabalho em andamento e dados salvos no navegador.",
    5: "Controle de gastos com orçamento e distribuição automática de aportes, feito em dupla com pull requests revisados.",
    6: "Agendamento de salão que conversa com uma API: horários livres, conflitos, almoço e cancelamento com antecedência.",
    7: "Loja com catálogo, filtros na URL, carrinho, frete por CEP e cupom, em Next.js com TypeScript.",
    8: "Ordens de serviço de uma assistência técnica em C#: fluxo de status com regras, orçamento, garantia e relatórios.",
    9: "O banco de dados de um PDV: modelo, restrições que recusam dado errado, 10 consultas e índices medidos.",
    10: "A API de uma clínica veterinária com login, papéis, agenda sem conflito e carteira de vacinação.",
    11: "Reservas das áreas do condomínio em que cada regra nasce como um teste que falha.",
    12: "Um sistema completo no ar, com testes, Docker e deploy: PDV, entregas em tempo real ou plataforma de exercícios.",
}

IA_GERAL = "Vale o guia <a href=\"ia.html\">Usar IA do jeito certo</a>. Na entrega, você explica em até 5 minutos qualquer trecho que o professor apontar: quem não sabe explicar o próprio código não passa."


def lista(itens, ordenada=False):
    tag = "ol" if ordenada else "ul"
    return f"<{tag}>" + "".join(f"<li>{i}</li>" for i in itens) + f"</{tag}>"


def tabela(cab, linhas, classe="tabela"):
    th = "".join(f'<th scope="col">{c}</th>' for c in cab)
    tr = "".join("<tr>" + "".join(f"<td>{c}</td>" for c in l) + "</tr>" for l in linhas)
    return f'<div class="tabela-rolagem"><table class="{classe}"><thead><tr>{th}</tr></thead><tbody>{tr}</tbody></table></div>'


def marco(k, titulo, funciona, dicas):
    """dicas: (pergunta, conceito, [(href, texto), ...])"""
    pergunta, conceito, links = dicas
    estudo = "".join(f'<li><a href="{h}">{t}</a></li>' for h, t in links)
    return f'''<div class="marco" id="marco-{k}">
  <h3>Marco {k}: {titulo}</h3>
  <p><strong>Funciona quando:</strong> {funciona}</p>
  <p>Travou? Abra uma dica de cada vez, e só depois de tentar de verdade.</p>
  <div class="dicas">
    <details><summary>Dica 1: uma pergunta para pensar</summary><div><p>{pergunta}</p></div></details>
    <details><summary>Dica 2: o conceito que resolve</summary><div><p>{conceito}</p></div></details>
    <details><summary>Dica 3: onde estudar isso</summary><div><ul>{estudo}</ul></div></details>
  </div>
</div>'''


def resultado_midia(n):
    """Vídeo e imagens gravados pelo professor, se existirem na pasta."""
    pasta = f"{SAIDA}/midia-projetos"
    video = f"midia-projetos/projeto-{n}.mp4"
    imagens = sorted(f for f in os.listdir(pasta) if f.startswith(f"projeto-{n}-") and f.endswith((".webp", ".png", ".jpg"))) if os.path.isdir(pasta) else []
    partes = []
    if os.path.exists(f"{SAIDA}/{video}"):
        partes.append(f'<video controls preload="metadata" src="{video}"></video>')
    for img in imagens:
        partes.append(f'<img src="midia-projetos/{img}" alt="Resultado esperado do Projeto {n}: {esc(img.rsplit(".", 1)[0].split("-", 2)[-1].replace("-", " "))}" loading="lazy">')
    if not partes:
        partes.append('<p class="resultado-pendente">O vídeo do resultado esperado está sendo gravado. Enquanto isso, a tabela de casos de teste abaixo mostra exatamente o que o seu projeto precisa fazer.</p>')
    return '<div class="resultado-esperado">' + "".join(partes) + "</div>"


def pagina_projeto(n, lead, praticar, pre_req, nota_pre, requisitos, regras, casos_cab, casos, criterios, marcos, perguntas, erros, ia, desafio, extra_regras=""):
    t = MAPA[n - 1]
    _, titulo, inspirado, etapa, modulos, tecnologias, duracao, formato, *_ = t
    disponiveis = [m for m in MAPA if m[8]]
    mapa_html = '<nav class="trilha-mapa" aria-label="Projetos disponíveis"><ol>' + "".join(
        f'<li><a href="projeto-{m[0]}.html" data-passo="p{m[0]}" data-total="{TOTAIS[m[0]]}"{" aria-current=\"step\"" if m[0] == n else ""}><span class="mapa-num">{m[0]}</span><span class="mapa-nome">{m[1]}</span></a></li>'
        for m in disponiveis) + "</ol></nav>"
    ant = f'<a class="passo-ant" href="projeto-{n-1}.html"><span>Projeto anterior</span>{n-1}. {MAPA[n-2][1]}</a>' if n > 1 else '<a class="passo-ant" href="projetos.html"><span>Voltar</span>Todos os projetos</a>'
    prox = f'<a class="passo-prox" href="projeto-{n+1}.html"><span>Próximo projeto</span>{n+1}. {MAPA[n][1]}</a>' if n < len(disponiveis) else '<a class="passo-prox" href="projetos.html"><span>Próximos</span>Ver todos os projetos</a>'
    pronto_itens = "\n".join(f'    <li><label><input type="checkbox" data-progresso="passo-p{n}-{i}"> {c}</label></li>' for i, c in enumerate(criterios, 1))
    casos_html = tabela(casos_cab, casos, "tabela casos")
    marcos_html = "\n".join(marco(i, *m) for i, m in enumerate(marcos, 1))
    erros_html = "\n".join(erro(a, f"<p>{b}</p>") for a, b in erros)
    html = f'''<!DOCTYPE html>
<!--
  PROJETO {n} — {titulo}
  Enunciado, resultado esperado e dicas em camadas. Sem código pronto: o aluno constrói do zero.
  As caixas "Pronto quando" guardam o progresso (js/trilha.js, prefixo "projeto:").
-->
<html lang="pt-BR">
<head>
{HEAD_COMUM}
  <script src="js/trilha.js" defer></script>
  <title>Projeto {n}: {esc(titulo)} — Catálogo do HTML</title>
  <meta name="description" content="{esc(RESUMOS[n])}">
</head>
<body class="pagina-trilha" data-prefixo="projeto:">
{cabecalho_html("projetos")}
  <main id="conteudo">
    <section class="heroi heroi-curto" id="inicio">
      <div class="container heroi-conteudo">
        <nav aria-label="Você está em" class="migalhas">
          <ol>
            <li><a href="index.html">Início</a></li>
            <li><a href="projetos.html">Projetos</a></li>
            <li aria-current="page">Projeto {n}</li>
          </ol>
        </nav>
        <p class="heroi-antetitulo">Projeto {n} de 12 · Etapa {etapa} · {duracao} · {formato.lower()}</p>
        <h1 class="heroi-titulo">{esc(titulo)}</h1>
        <p class="heroi-texto">{lead}</p>
      </div>
    </section>

    <div class="container trilha-corpo">
      {mapa_html}
      <p><span class="selo">Inspirado no {esc(inspirado)}</span> <span class="selo">{esc(tecnologias)}</span> <span class="selo">Módulo {modulos}</span></p>

<section class="trilha-secao" id="resultado">
  <h2>1. O resultado esperado</h2>
  <p>É aqui que você precisa chegar. Não existe código para copiar: você constrói do zero e confere cada linha da tabela no seu projeto.</p>
  {resultado_midia(n)}
  <h3>Casos de teste</h3>
  <p>Faça exatamente estas entradas. A saída do seu projeto precisa ser igual, número por número.</p>
  {casos_html}
</section>

<section class="trilha-secao" id="antes">
  <h2>2. Antes de começar</h2>
  <p><strong>O que você vai praticar:</strong> {praticar}</p>
  {teoria(*pre_req)}
  {f'<p>{nota_pre}</p>' if nota_pre else ''}
</section>

<section class="trilha-secao" id="requisitos">
  <h2>3. O que o sistema precisa fazer</h2>
  <h3>Requisitos</h3>
  {lista(requisitos, True)}
  <h3>Regras de negócio</h3>
  {lista(regras)}
  {extra_regras}
</section>

<section class="trilha-secao" id="marcos">
  <h2>4. Construa em 4 marcos</h2>
  <p>Não tente fazer tudo de uma vez. Termine um marco, confira que funciona, faça um commit e só então passe para o próximo.</p>
  {marcos_html}
</section>

<section class="trilha-secao" id="pensar">
  <h2>5. Perguntas para pensar antes de programar</h2>
  <p>Responda no papel (ou no README). Elas apontam as armadilhas do projeto antes de você cair nelas. As respostas não estão aqui de propósito: é o que o projeto ensina.</p>
  {lista(perguntas)}
  {erros_html}
  {usar_ia(ia[0], ia[1])}
</section>

<section class="trilha-secao" id="entrega">
  <h2>6. Entrega</h2>
  {lista(["Repositório no GitHub, com commits ao longo do trabalho (um por marco, pelo menos).", "README com o que o projeto faz, como rodar, as decisões que você tomou e o que ficou de fora.", "Link publicado no GitHub Pages." if n in (2, 3, 4) else "Link publicado, ou o código com instruções para rodar.", "Vídeo de até 2 minutos mostrando os casos de teste funcionando."])}
  <p>A avaliação é a mesma nos 12 projetos: funciona como pedido (50%), qualidade do código (20%), documentação (15%) e explicação oral (15%). Detalhes em <a href="projetos.html#avaliacao">Como os projetos funcionam</a>.</p>
  {tenta(f"<p><strong>Desafio extra:</strong> {desafio}</p>")}
</section>
      <section class="pronto-quando" aria-labelledby="pronto-titulo">
  <h2 id="pronto-titulo">Pronto quando</h2>
  <p>Marque cada critério de aceite só depois de conferir. O progresso fica salvo e aparece no mapa dos projetos.</p>
  <ul data-total="{len(criterios)}">
{pronto_itens}
  </ul>
</section>
      <nav class="passo-nav" aria-label="Navegação entre projetos">
        {ant}
        {prox}
      </nav>
    </div>
  </main>

{RODAPE}'''
    open(f"{SAIDA}/projeto-{n}.html", "w", encoding="utf-8").write(html)


JS = "doc-js.html"
TOTAIS = {1: 4, 2: 6, 3: 5, 4: 6}

# =============================================================== PROJETO 1
pagina_projeto(
    1,
    "A padaria do bairro fecha as vendas numa calculadora e erra o troco no fim do dia. O dono quer um programa que some a venda, aplique o desconto certo e diga exatamente quais cédulas e moedas devolver.",
    "variáveis, decisões, laços, funções, listas de objetos e o cuidado que dinheiro exige num programa.",
    [(f"{JS}#variaveis", "Variáveis e tipos"), (f"{JS}#operadores", "Operadores: aritmética, comparação e resto da divisão"),
     (f"{JS}#lacos", "Laços de repetição"), (f"{JS}#funcoes", "Funções"), (f"{JS}#listas-objetos", "Listas e objetos"), (f"{JS}#erros", "Erros e o Console")],
    "O módulo 1 (Lógica de programação) está em produção. Até ele sair, o capítulo de JavaScript do módulo 2 explica tudo o que este projeto usa.",
    ["RF01 — Adicionar item: nome, preço unitário e quantidade.", "RF02 — Remover item e alterar a quantidade.",
     "RF03 — Mostrar subtotal, desconto e total a qualquer momento.", "RF04 — Finalizar com dinheiro, Pix ou cartão.",
     "RF05 — No dinheiro, mostrar o troco separado por cédulas e moedas.", "RF06 — Emitir um recibo em texto com itens, totais e forma de pagamento."],
    ["RN01 — Desconto de 5% no subtotal acima de R$ 100,00 e de 10% acima de R$ 200,00 (acima = maior que, não igual).",
     "RN02 — Todo valor é guardado em centavos, como número inteiro. Nenhuma conta usa número com vírgula.",
     "RN03 — Troco com o menor número de peças: R$ 100, 50, 20, 10, 5, 2, 1 e 0,50, 0,25, 0,10, 0,05, 0,01.",
     "RN04 — Cartão em até 3 vezes sem juros, só para total acima de R$ 60,00. Os centavos que sobram da divisão vão na primeira parcela.",
     "RN05 — Quantidade e preço precisam ser maiores que zero. Venda sem itens não finaliza."],
    ["#", "Entrada", "Saída esperada"],
    [("1", "2 × R$ 12,50 e 1 × R$ 80,00; dinheiro R$ 100,00", "Subtotal R$ 105,00 · desconto R$ 5,25 · total R$ 99,75 · troco R$ 0,25 = 1 moeda de 0,25"),
     ("2", "Itens somando R$ 250,00; dinheiro R$ 300,00", "Desconto R$ 25,00 · total R$ 225,00 · troco R$ 75,00 = 1 × 50 + 1 × 20 + 1 × 5"),
     ("3", "Subtotal R$ 100,00; cartão em 3 vezes", "Sem desconto · parcelas R$ 33,34 + R$ 33,33 + R$ 33,33"),
     ("4", "Total R$ 45,90; cartão em 2 vezes", "Recusado: \"Parcelamento só acima de R$ 60,00\""),
     ("5", "Total R$ 99,75; dinheiro R$ 90,00", "Recusado: \"Faltam R$ 9,75\""),
     ("6", "Nenhum item; finalizar", "Recusado: \"Adicione pelo menos um item\""),
     ("7", "Preço 0 ou quantidade -1", "Item não entra, com a mensagem do erro")],
    ["Os 7 casos de teste dão exatamente a saída da tabela.", "Nenhuma conta de dinheiro usa números com vírgula (RN02).",
     "Cada regra está numa função própria, com nome que diz o que ela faz.", "O recibo mostra itens, subtotal, desconto, total, forma de pagamento e troco ou parcelas."],
    [
        ("As funções de cálculo no Console", "você chama à mão, no Console (F12), as funções de desconto e de troco, e elas devolvem os valores dos casos 1 e 2.",
         ("O preço é R$ 12,50. Qual número <em>inteiro</em> representa esse valor, sem vírgula? E como você transforma esse número de volta no texto \"12,50\" só na hora de mostrar?",
          "Guarde tudo em centavos (1250) e só divida por 100 para mostrar. O troco com menos peças é um algoritmo \"guloso\": tente sempre a maior peça que ainda cabe no valor que falta. A divisão inteira (<code>Math.floor</code>) diz quantas peças cabem; o resto (<code>%</code>) é o que sobra para as próximas.",
          [(f"{JS}#variaveis", "Variáveis e tipos"), (f"{JS}#operadores", "Operadores, incluindo o resto da divisão"), (f"{JS}#funcoes", "Funções que recebem e devolvem valores")])),
        ("A venda como uma lista de itens", "você adiciona, remove e muda a quantidade de itens, e o subtotal, o desconto e o total se ajustam sozinhos.",
         ("Um item tem nome, preço e quantidade. Que tipo de dado guarda essas três informações juntas? E vários itens?",
          "Um array de objetos. Não guarde o subtotal numa variável que você atualiza à mão: calcule de novo, percorrendo a lista, sempre que precisar. Assim ele nunca fica desatualizado.",
          [(f"{JS}#listas-objetos", "Listas e objetos"), (f"{JS}#lacos", "Percorrer uma lista com laço")])),
        ("As formas de pagamento, com os erros", "os casos 3 a 7 dão as mensagens da tabela, e nenhum erro derruba o programa.",
         ("Quando o pagamento dá certo, a função devolve o quê? E quando não dá? Como quem chamou a função sabe a diferença?",
          "Duas saídas comuns: devolver um objeto do tipo <code>{ ok: false, mensagem: \"...\" }</code>, ou lançar um erro com <code>throw</code> e tratar com <code>try/catch</code>. Nas parcelas, divida os centavos com divisão inteira; o resto vai para a primeira parcela.",
          [(f"{JS}#erros", "Erros e try/catch"), (f"{JS}#operadores", "Divisão inteira e resto")])),
        ("A página com formulário", "uma página simples usa as MESMAS funções dos marcos anteriores e mostra o recibo na tela.",
         ("Suas funções de cálculo precisam mudar para funcionar na página? Se precisarem, o que isso diz sobre como elas foram escritas?",
          "Não deveriam mudar. Separe as funções que só calculam das que mexem na tela. A página lê os campos no evento <code>submit</code> (com <code>preventDefault</code>), chama as funções de cálculo e escreve o resultado com <code>textContent</code>.",
          [(f"{JS}#dom", "Pegar e mudar elementos da página"), (f"{JS}#eventos", "Eventos"), ("doc-html.html#formularios", "Formulários no HTML")])),
    ],
    ["Por que 0,1 + 0,2 não dá 0,3 no JavaScript, e como os centavos resolvem isso?",
     "O troco com o menor número de peças: qual peça você tenta primeiro, e até quando?",
     "Quais funções só calculam e quais mostram coisas na tela? Por que separar as duas?"],
    [("o total aparece como R$ 99,74999.", "Você fez a conta com reais e vírgula. Converta para centavos logo na entrada: <code>Math.round(valor * 100)</code>. Daí em diante, só números inteiros."),
     ("o desconto aparece numa compra de exatamente R$ 100,00.", "A regra diz \"acima de\": use <code>&gt;</code>, não <code>&gt;=</code>. Releia as regras de negócio com lápis na mão: cada palavra vira código."),
     ("a página trava ao calcular o troco.", "Um laço que nunca termina: dentro dele, o valor que falta não está diminuindo. Use <code>console.log</code> dentro do laço para ver o valor a cada volta.")],
    ("pedir para explicar por que 0,1 + 0,2 dá 0,30000000000000004, explicar uma mensagem de erro do Console, ou revisar os nomes das suas funções depois de prontas.",
     "pedir a função de troco ou a de parcelas. Elas são o coração do projeto: é resolvendo essas duas que você aprende lógica."),
    "o caixa começa o dia com uma quantidade limitada de cada cédula e moeda. O troco passa a respeitar o que existe no caixa e avisa quando não há troco possível.",
)

# =============================================================== PROJETO 2
CSS = "doc-css.html"
pagina_projeto(
    2,
    "O museu da cidade quer contar a história de Barbacena (ou de uma família, de um time, de uma escola: você escolhe) numa página que funcione bem no celular, porque é pelo celular que os visitantes vão abrir, lendo um QR Code na entrada.",
    "HTML semântico, CSS responsivo com mobile first, variáveis CSS, imagens leves e publicação com a prévia certa no WhatsApp.",
    [("doc-html.html#semantica", "Semântica: a tag certa para cada coisa"), ("doc-html.html#textos-links", "Textos, links e imagens"),
     (f"{CSS}#caixa", "Modelo de caixa"), (f"{CSS}#position", "Position"), (f"{CSS}#responsivo", "Responsivo e @media"),
     (f"{CSS}#variaveis", "Variáveis CSS"), ("passo-9-pages.html", "Passo 9: publicar no GitHub Pages")],
    None,
    ["RF01 — Pelo menos 10 acontecimentos, cada um com data, título, texto curto e foto.", "RF02 — Um menu que leva a cada período (décadas ou fases) pela âncora.",
     "RF03 — No celular, a linha do tempo é uma coluna com a linha à esquerda; no computador, os acontecimentos alternam de lado.",
     "RF04 — Um acontecimento em destaque, visualmente diferente dos outros.", "RF05 — Rodapé com as fontes de cada informação e de cada foto.",
     "RF06 — Publicada no GitHub Pages, com o link e a imagem aparecendo certos ao compartilhar no WhatsApp."],
    ["RN01 — Só HTML e CSS. Nenhuma linha de JavaScript.",
     "RN02 — HTML semântico: a linha do tempo é uma lista ordenada, cada acontecimento um artigo, cada data uma tag de data com o valor que as máquinas leem.",
     "RN03 — Cada foto com texto alternativo, largura e altura, e no máximo 300 KB.",
     "RN04 — Cores e espaços em variáveis CSS. Nenhuma cor repetida fora delas."],
    ["#", "Teste", "Resultado esperado"],
    [("1", "Validador do W3C (validator.w3.org) com o seu endereço", "Nenhum erro"),
     ("2", "Lighthouse no modo celular", "Acessibilidade 90 ou mais e Desempenho 90 ou mais"),
     ("3", "Janela com 360 px de largura (F12, modo celular)", "Uma coluna, linha à esquerda, sem rolagem para o lado"),
     ("4", "Janela com 1280 px", "Os acontecimentos alternam de lado"),
     ("5", "Só a tecla Tab, do topo até o menu", "O foco aparece em todos os links do menu"),
     ("6", "Verificador do projeto", "Nenhum item vermelho"),
     ("7", "Mandar o link no WhatsApp", "Aparecem título, descrição e imagem"),
     ("8", "Desligar o CSS (F12, ou abrir sem o estilo)", "A página continua legível, na ordem certa")],
    ["O validador do W3C não mostra nenhum erro.", "Lighthouse no celular: Acessibilidade 90 ou mais e Desempenho 90 ou mais.",
     "Em 360 px não existe rolagem para o lado; em 1280 px os itens alternam de lado.", "Navegando só com Tab, o foco aparece em todo link do menu.",
     "O Verificador do projeto não mostra nenhum item vermelho.", "O link compartilhado mostra título, descrição e imagem."],
    [
        ("O conteúdo inteiro em HTML puro", "a página, ainda sem nenhum CSS, é lida de cima a baixo como um documento, com todos os acontecimentos na ordem.",
         ("Uma linha do tempo é uma sequência em que a ordem importa. Qual lista do HTML diz \"a ordem importa\"? E qual tag diz \"isto é uma data\" para o Google e para o leitor de tela?",
          "Uma lista ordenada <code>&lt;ol&gt;</code>, com um <code>&lt;article&gt;</code> em cada item e a data em <code>&lt;time datetime=\"1791-08-14\"&gt;</code>. Escreva tudo antes de qualquer CSS: se a página faz sentido assim, a estrutura está certa.",
          [("doc-html.html#semantica", "Semântica"), ("doc-html.html#estrutura", "Estrutura da página"), ("doc-html.html#textos-links", "Imagens e o atributo alt")])),
        ("A versão de celular", "em 360 px a linha do tempo é uma coluna, com a linha vertical à esquerda e um marcador em cada acontecimento.",
         ("A linha vertical é conteúdo ou decoração? Então ela deve existir no HTML ou ser desenhada pelo CSS?",
          "Decoração é CSS. A linha pode ser a borda esquerda da lista; o marcador de cada item, um <code>::before</code> com <code>position: absolute</code> dentro de um item com <code>position: relative</code>.",
          [(f"{CSS}#caixa", "Borda, padding e margin"), (f"{CSS}#position", "Position relative e absolute"), (f"{CSS}#variaveis", "Variáveis CSS")])),
        ("A versão de computador", "a partir de uma largura que você escolheu, os acontecimentos alternam de lado, e o destaque continua diferente.",
         ("Você escreveu o CSS do celular primeiro. A partir de qual largura o layout deve mudar? E como escolher \"um item sim, outro não\" sem pôr uma classe em cada um?",
          "Mobile first: o <code>@media (min-width: ...)</code> só acrescenta o que muda no computador. <code>:nth-child(even)</code> escolhe os itens pares. Grid com duas colunas ou margens resolvem a alternância.",
          [(f"{CSS}#responsivo", "Responsivo e @media"), (f"{CSS}#seletores", "Seletores, incluindo :nth-child"), (f"{CSS}#grid", "Grid")])),
        ("Fotos leves, prévia no WhatsApp e publicação", "cada foto pesa menos de 300 KB, a página está no GitHub Pages e o link mostra a prévia.",
         ("Quanto pesa cada foto? E em que tamanho ela aparece na tela? Faz sentido enviar uma foto de 4000 px para aparecer com 600?",
          "Redimensione cada foto para a largura em que ela aparece e salve em WebP ou JPG (qualidade perto de 80). Coloque <code>width</code> e <code>height</code>. Para a prévia, as metatags <code>og:title</code>, <code>og:description</code> e <code>og:image</code>, esta com o endereço completo (https://...).",
          [("passo-9-pages.html", "Passo 9: publicar e caprichar no link compartilhado"), ("doc-html.html#textos-links", "Imagens")])),
    ],
    ["Se o CSS não carregar, a página ainda faz sentido? O que isso diz sobre o seu HTML?",
     "A linha vertical é conteúdo ou decoração? Onde ela deve ser desenhada, no HTML ou no CSS?",
     "Qual seletor escolhe \"um item sim, outro não\" sem colocar uma classe em cada um?"],
    [("no celular aparece rolagem para o lado.", "Algo está mais largo que a tela: uma imagem sem <code>max-width: 100%</code> ou uma largura fixa em <code>px</code>. No F12, passe o mouse pelos elementos até achar o que passa da borda."),
     ("a imagem não aparece na prévia do WhatsApp.", "O <code>og:image</code> precisa do endereço completo, começando com https. O WhatsApp também guarda a prévia antiga: teste com o link seguido de <code>?v=2</code>."),
     ("todos os acontecimentos ficam do mesmo lado.", "O <code>:nth-child</code> precisa estar no elemento que se repete como filho da lista (o <code>li</code>), não no <code>article</code> de dentro, que é sempre o primeiro filho do seu <code>li</code>.")],
    ("pedir para revisar a semântica e a acessibilidade do seu HTML pronto, ou explicar por que um seletor não pegou o elemento que você queria.",
     "pedir o CSS da linha do tempo. Montar esse layout é o que o projeto ensina."),
    "uma versão para impressão (<code>@media print</code>) em uma coluna, sem menu, com o endereço de cada fonte escrito ao lado do link.",
)

# =============================================================== PROJETO 3
pagina_projeto(
    3,
    "Você vai precisar mostrar o que sabe fazer. Um recrutador gasta menos de um minuto num portfólio: precisa ver quem é a pessoa, os projetos e como falar com ela, sem procurar.",
    "montar a página pelo JavaScript a partir de dados, filtros combinados, janela acessível, validação de formulário e lembrar escolhas do visitante.",
    [(f"{JS}#listas-objetos", "Listas e objetos"), (f"{JS}#dom", "Criar elementos na página"), (f"{JS}#eventos", "Eventos"),
     (f"{JS}#dados", "Guardar dados no navegador"), ("doc-html.html#formularios", "Formulários"), ("doc-html.html#acessibilidade", "Acessibilidade")],
    None,
    ["RF01 — Seções: apresentação, projetos, habilidades, contato.",
     "RF02 — Os cards de projeto são montados pelo JavaScript a partir de uma lista de objetos (nome, descrição, tecnologias, imagem, link do site, link do repositório).",
     "RF03 — Filtro por tecnologia: clicar em \"CSS\" mostra só os projetos que usam CSS; dá para combinar dois filtros.",
     "RF04 — Clicar num card abre uma janela com os detalhes do projeto.",
     "RF05 — Formulário de contato com validação (nome, e-mail, mensagem de 20 a 500 caracteres) e mensagem de confirmação.",
     "RF06 — Tema claro e escuro, lembrado na próxima visita."],
    ["RN01 — Acrescentar um projeto novo = acrescentar um objeto na lista. Nenhum HTML muda.",
     "RN02 — Com dois filtros ligados, aparecem os projetos que têm as duas tecnologias.",
     "RN03 — A janela fecha com Esc, com o botão fechar e clicando fora dela; ao fechar, o foco volta ao card.",
     "RN04 — Nenhum filtro ligado = todos os projetos. Nenhum resultado = mensagem \"Nenhum projeto com essas tecnologias\"."],
    ["#", "Ação", "Resultado esperado"],
    [("1", "Acrescentar um objeto na lista e recarregar", "Um card novo aparece, com a imagem e os links"),
     ("2", "Ligar \"CSS\" e depois \"JavaScript\"", "Só os projetos que têm as duas"),
     ("3", "Ligar um filtro que nenhum projeto tem", "A mensagem de nenhum resultado"),
     ("4", "Abrir um card e apertar Esc", "A janela fecha e o foco volta ao card"),
     ("5", "Enviar o formulário com e-mail \"ana@\"", "O erro aparece junto do campo, e nada é enviado"),
     ("6", "Ligar o tema escuro e recarregar", "Continua escuro")],
    ["Os 6 casos de teste funcionam.", "Tudo é usável só com o teclado.", "Lighthouse 90 ou mais em Acessibilidade e em Boas práticas.",
     "Publicado no endereço principal (seu-usuario.github.io).", "Os projetos 1 e 2 já estão no portfólio, com os links funcionando."],
    [
        ("A página estática", "as quatro seções existem, o menu leva a cada uma e dois cards de projeto estão escritos à mão no HTML.",
         ("O recrutador abre o seu site. O que ele precisa ver sem rolar a página?",
          "Seu nome e o que você faz no <code>h1</code>, um botão para os projetos e outro para o contato. Os dois cards escritos à mão são o molde que o JavaScript vai repetir no próximo marco.",
          [("doc-html.html#semantica", "Semântica"), (f"{CSS}#flexbox", "Flexbox"), (f"{CSS}#grid", "Grid")])),
        ("Os cards gerados pela lista", "você apaga os cards escritos à mão e eles aparecem do mesmo jeito, montados a partir de uma lista no JavaScript.",
         ("Compare os dois cards que você escreveu à mão. Quais partes mudam de um projeto para outro? Essas partes são os seus dados.",
          "Um array de objetos com essas partes e uma função que recebe um projeto e devolve o elemento do card, criado com <code>createElement</code> e preenchido com <code>textContent</code>. Um laço chama a função para cada projeto e coloca o card na página.",
          [(f"{JS}#listas-objetos", "Listas e objetos"), (f"{JS}#dom", "createElement, textContent e append"), (f"{JS}#funcoes", "Funções")])),
        ("Filtros e janela de detalhes", "os casos 2, 3 e 4 funcionam.",
         ("Com \"CSS\" e \"JavaScript\" ligados, deve aparecer o projeto que tem qualquer um dos dois, ou só o que tem os dois? Qual método de lista pergunta \"todos cumprem?\"",
          "Guarde os filtros ligados num array e mostre o projeto se <em>todo</em> filtro ligado estiver nas tecnologias dele (<code>every</code> + <code>includes</code>). Para a janela, a tag <code>&lt;dialog&gt;</code> com <code>showModal()</code> já fecha com Esc; guarde qual card abriu para devolver o foco a ele.",
          [(f"{JS}#listas-objetos", "Métodos de lista: every, some, includes, filter"), (f"{JS}#eventos", "Eventos"), ("doc-html.html#acessibilidade", "Foco e teclado")])),
        ("Formulário, tema e publicação", "os casos 5 e 6 funcionam e o site está no ar no endereço principal.",
         ("Onde o site guarda uma escolha do visitante para a próxima visita? E o que o HTML já valida sozinho, antes de qualquer JavaScript?",
          "<code>localStorage</code> guarda o tema. No formulário, <code>required</code>, <code>type=\"email\"</code>, <code>minlength</code> e <code>maxlength</code> já validam; o JavaScript chama <code>checkValidity()</code> e mostra a mensagem ao lado do campo. O endereço principal é um repositório chamado exatamente <code>seu-usuario.github.io</code>.",
          [(f"{JS}#dados", "localStorage"), ("doc-html.html#formularios", "Validação de formulários"), ("passo-9-pages.html", "Passo 9: GitHub Pages")])),
    ],
    ["Que forma de dado guarda \"um projeto\" com todas as informações dele? E \"todos os projetos\"?",
     "Filtrar é esconder cards que já existem ou montar a lista de novo? Quais são as vantagens de cada jeito?",
     "Qual elemento do HTML já é uma janela e já fecha com Esc?"],
    [("o card mostra &lt;strong&gt; escrito, ou um texto do projeto quebra a página.", "Use <code>textContent</code> para colocar dados na página, nunca <code>innerHTML</code> com texto vindo de uma lista: além de quebrar, é uma porta para código malicioso."),
     ("com um filtro ligado e depois desligado, não volta a mostrar tudo.", "Confira o que a sua conta faz com a lista de filtros vazia. <code>every</code> numa lista vazia devolve <code>true</code> (todos aparecem); <code>some</code> devolve <code>false</code> (nenhum aparece)."),
     ("o tema pisca claro antes de ficar escuro.", "O tema é aplicado depois que a página já apareceu. Leia o <code>localStorage</code> e aplique o tema o mais cedo possível.")],
    ("pedir para explicar a diferença entre <code>every</code> e <code>some</code>, ou para revisar a acessibilidade da janela depois de pronta.",
     "pedir a função que monta os cards ou a do filtro. É com elas que você aprende a transformar dados em página."),
    "a lista de projetos sai de um arquivo <code>projetos.json</code> lido com <code>fetch</code>, e o portfólio mostra quantos commits cada repositório tem, pela API pública do GitHub.",
)

# =============================================================== PROJETO 4
pagina_projeto(
    4,
    "Uma pequena equipe controla as tarefas num grupo de mensagens e perde o que foi combinado. Ela precisa de um quadro simples, que funcione no navegador, sem login, onde cada pessoa veja o que está parado, o que está andando e o que terminou.",
    "estado da aplicação num array, redesenhar a tela a partir dele, arrastar e soltar com alternativa pelo teclado, ordenação, datas, e salvar, exportar e importar dados.",
    [(f"{JS}#listas-objetos", "Listas e objetos"), (f"{JS}#dom", "Mudar a página"), (f"{JS}#eventos", "Eventos"),
     (f"{JS}#dados", "localStorage e JSON"), (f"{JS}#erros", "Tratar erros")],
    None,
    ["RF1 — Três colunas fixas: A fazer, Fazendo e Feito.", "RF2 — Criar cartão com título (obrigatório), descrição, prioridade (baixa, média, alta) e data de entrega (opcional).",
     "RF3 — Editar e excluir cartão. Excluir pede confirmação.", "RF4 — Mover cartão entre colunas arrastando com o mouse e por botões ou teclado.",
     "RF5 — Cada coluna mostra quantos cartões tem.", "RF6 — Cartão com data de entrega vencida e fora de Feito aparece destacado.",
     "RF7 — Filtro por prioridade e busca por texto no título.", "RF8 — Tudo continua lá depois de fechar e abrir o navegador.",
     "RF9 — Exportar o quadro para um arquivo .json e importar de volta."],
    ["RN1 — A coluna Fazendo aceita no máximo 3 cartões. O 4º é recusado com um aviso, e o cartão volta para onde estava.",
     "RN2 — Título vazio ou só com espaços não cria cartão.", "RN3 — Cartão em Feito nunca aparece como atrasado.",
     "RN4 — Dentro da coluna, a ordem é: prioridade alta primeiro, depois a data de entrega mais próxima.",
     "RN5 — Importar um arquivo inválido não apaga o quadro atual; mostra o erro."],
    ["Situação", "Ação", "Resultado esperado"],
    [("Fazendo tem 3 cartões", "Arrastar um 4º para Fazendo", "Aviso \"Limite de 3 cartões em Fazendo\"; o cartão continua na coluna de origem"),
     ("Título \"   \"", "Clicar em Criar", "Nenhum cartão criado; mensagem no campo"),
     ("Hoje é 10/05; cartão em A fazer com entrega 09/05", "Abrir o quadro", "Cartão destacado como atrasado"),
     ("O mesmo cartão", "Mover para Feito", "O destaque some"),
     ("5 cartões criados", "Fechar e reabrir o navegador", "Os 5 cartões, nas mesmas colunas e na mesma ordem"),
     ("Arquivo .json com texto qualquer", "Importar", "Mensagem de erro; quadro atual intacto"),
     ("Cartões: média 12/05, alta 20/05, alta 15/05", "Ver a coluna", "Ordem: alta 15/05, alta 20/05, média 12/05")],
    ["Todos os casos de teste passam.", "Dá para usar o quadro inteiro só com o teclado.", "Funciona no celular sem rolagem horizontal.",
     "Nenhum erro no Console (F12).", "HTML, CSS e JS em arquivos separados; nada de onclick no HTML.", "Publicado no GitHub Pages, com README que explica como usar."],
    [
        ("Colunas e cartões desenhados a partir dos dados", "as três colunas aparecem com cartões de exemplo que estão numa lista no JavaScript, e não escritos no HTML.",
         ("O quadro na tela e a lista no JavaScript: qual dos dois é a verdade? Se eles discordarem, qual vale?",
          "A lista de cartões (cada um com id, coluna, título, prioridade e data) é a verdade. Uma função apaga as colunas e desenha tudo de novo a partir da lista. Toda ação do usuário muda a lista e chama essa função.",
          [(f"{JS}#listas-objetos", "Listas e objetos"), (f"{JS}#dom", "Criar e apagar elementos")])),
        ("Criar, editar, excluir e salvar", "os casos 2 e 5 funcionam.",
         ("O localStorage só guarda texto. Como transformar uma lista de objetos em texto, e o texto de volta em lista? E como saber qual cartão editar se dois têm o mesmo título?",
          "<code>JSON.stringify</code> para salvar e <code>JSON.parse</code> para ler (dentro de <code>try/catch</code>, para o caso de estar vazio ou estragado). Dê a cada cartão um id único na criação, por exemplo <code>crypto.randomUUID()</code>.",
          [(f"{JS}#dados", "localStorage e JSON"), (f"{JS}#erros", "try/catch")])),
        ("Mover, limitar e ordenar", "os casos 1 e 7 funcionam com o mouse e com o teclado.",
         ("Qual evento avisa \"soltaram algo aqui\"? E quem não usa mouse, como move um cartão?",
          "<code>draggable=\"true\"</code> no cartão; <code>dragstart</code> guarda o id; <code>dragover</code> precisa de <code>preventDefault()</code> para permitir soltar; <code>drop</code> muda a coluna na lista. Para o teclado, botões ← e → no cartão. O limite de 3 é conferido antes de mudar a coluna. Para ordenar, <code>sort</code> com uma função que compara a prioridade e, se empatar, a data.",
          [(f"{JS}#eventos", "Eventos"), (f"{JS}#funcoes", "Funções que comparam")])),
        ("Atrasados, filtros, exportar e importar", "os casos 3, 4 e 6 funcionam e o quadro está no ar.",
         ("Como comparar a data de entrega com hoje sem que o horário (e o fuso) atrapalhe?",
          "O campo <code>type=\"date\"</code> dá o texto AAAA-MM-DD; monte o \"hoje\" no mesmo formato com a data local e compare os textos. Cuidado: <code>toISOString()</code> usa o horário de Londres. Exportar: um <code>Blob</code> e um link com <code>download</code>. Importar: <code>input type=\"file\"</code> e <code>FileReader</code>; confira o conteúdo antes de trocar o quadro.",
          [(f"{JS}#dados", "Dados e JSON"), (f"{JS}#erros", "Tratar erros")])),
    ],
    ["Onde fica a \"verdade\" do quadro: no HTML da tela ou num array de objetos no JS? O que acontece se forem duas verdades diferentes?",
     "Depois de mudar o array, como a tela fica igual a ele? Vale redesenhar tudo ou só o cartão que mudou?",
     "O localStorage só guarda texto. Como guardar um array de objetos?",
     "Como comparar a data de entrega com hoje sem errar por causa do horário?"],
    [("ao recarregar, o quadro volta aos cartões de exemplo.", "O código salva o exemplo por cima antes de ler o que estava guardado. Ordem certa: ler o localStorage; se não houver nada, usar o exemplo."),
     ("o cartão não solta na outra coluna.", "Falta <code>preventDefault()</code> no evento <code>dragover</code> da coluna: sem ele, o navegador não permite soltar."),
     ("à noite, um cartão com entrega para hoje aparece atrasado.", "É o fuso horário: <code>toISOString()</code> devolve a data de Londres, que já é amanhã. Monte a data de hoje com <code>getFullYear</code>, <code>getMonth</code> e <code>getDate</code>.")],
    ("pedir para explicar a ordem dos eventos de arrastar e soltar, ou por que o <code>sort</code> está ordenando errado, mostrando o seu código.",
     "pedir a função que desenha o quadro ou o arrastar e soltar prontos. Organizar o estado e redesenhar a tela é a habilidade que este projeto treina."),
    "histórico de cada cartão (\"criado em\", \"movido para Fazendo em\") e um botão Desfazer para a última ação.",
)

# =============================================================== MAPA (projetos.html)
def cartao_hub(m):
    n, titulo, inspirado, etapa, modulos, tec, dur, formato, disp, libera = m
    acesso = '<span class="selo selo-ok">Grátis</span>' if n == 1 else '<span class="selo">Formação</span>'
    if disp:
        cabeca = f'<h3><a href="projeto-{n}.html">{esc(titulo)}</a> {acesso}</h3>'
        lado = f'''<div class="trilha-card-progresso" data-passo="p{n}" data-total="{TOTAIS[n]}">
            <div class="barrinha"><span></span></div>
            <p><span class="feitos">0</span> de {TOTAIS[n]}</p>
          </div>'''
    else:
        cabeca = f'<h3>{esc(titulo)}</h3>'
        lado = f'<p class="ajuda">Libera junto com {esc(libera)}</p>'
    return f'''        <li class="trilha-card">
          <span class="trilha-card-num" aria-hidden="true">{n}</span>
          <div class="trilha-card-corpo">
            {cabeca}
            <p>{RESUMOS[n]}</p>
            <p class="trilha-card-tempo">Inspirado no {esc(inspirado)} · {esc(tec)} · {dur} · {formato.lower()}</p>
          </div>
          {lado}
        </li>'''


etapas_html = ""
for etapa, nome in [(1, "Etapa 1: lógica"), (2, "Etapa 2: HTML, CSS e JavaScript"), (3, "Etapa 3: equipe e web"), (4, "Etapa 4: front-end moderno"),
                    (5, "Etapa 5: C# e orientação a objetos"), (6, "Etapa 6: banco de dados"), (7, "Etapa 7: API com login"), (8, "Etapa 8: testes"), (9, "Etapa 9: sistema completo")]:
    itens = "\n".join(cartao_hub(m) for m in MAPA if m[3] == etapa)
    etapas_html += f'''      <section class="projetos-etapa" aria-labelledby="etapa-{etapa}">
        <h2 id="etapa-{etapa}">{nome}</h2>
        <ol class="trilha-lista">
{itens}
        </ol>
      </section>
'''

hub = f'''<!DOCTYPE html>
<!--
  PROJETOS — o mapa dos 12 projetos da formação
  Cada projeto tem enunciado, resultado esperado e dicas em camadas; nenhum traz código pronto.
-->
<html lang="pt-BR">
<head>
{HEAD_COMUM}
  <script src="js/trilha.js" defer></script>
  <title>Projetos — Catálogo do HTML</title>
  <meta name="description" content="12 projetos inspirados em sistemas reais, do primeiro programa ao sistema completo no ar. Você recebe o enunciado e o resultado esperado; o código você escreve do zero.">
</head>
<body class="pagina-trilha" data-prefixo="projeto:">
{cabecalho_html("projetos")}
  <main id="conteudo">
    <section class="heroi heroi-curto" id="inicio">
      <div class="container heroi-conteudo">
        <p class="heroi-antetitulo">12 projetos · do primeiro programa ao sistema no ar</p>
        <h1 class="heroi-titulo">Projetos</h1>
        <p class="heroi-texto">Cada projeto nasce de um sistema real e chega com o problema do cliente, as regras, o resultado esperado e dicas em camadas. O código você escreve do zero: é assim que se aprende a programar de verdade.</p>
      </div>
    </section>

    <div class="container trilha-corpo">
      <section class="trilha-secao" id="como">
        <h2>Como os projetos funcionam</h2>
        {tabela(["Você recebe", "Você não recebe"], [
            ("O problema do cliente, contado como história", "Código pronto, repositório pronto ou esqueleto do projeto"),
            ("Requisitos numerados (RF) e regras de negócio (RN)", "Passo a passo de código (\"crie o arquivo X e escreva Y\")"),
            ("Casos de teste: a entrada e a saída esperada", "A solução dos casos de teste"),
            ("Vídeo e imagens do resultado esperado", "Um site de demonstração (o código ficaria visível no navegador)"),
            ("4 marcos, cada um com 3 dicas que você abre uma de cada vez", "As respostas das perguntas para pensar"),
            ("Links para o capítulo da Documentação que ensina cada parte", ""),
        ])}
        <p>As dicas vêm em camadas de propósito: a primeira é uma pergunta, a segunda mostra o conceito, a terceira diz onde estudar. Abra a próxima só depois de tentar de verdade.</p>
      </section>

      <section class="trilha-secao" id="avaliacao">
        <h2>Entrega e avaliação</h2>
        <p>Todo projeto é entregue com repositório no GitHub (commits ao longo do trabalho), README, link publicado e um vídeo de até 2 minutos. A avaliação é a mesma nos 12:</p>
        {tabela(["Critério", "Peso", "Como se mede"], [
            ("Funciona como pedido", "50%", "Cada critério de aceite e cada caso de teste conferidos"),
            ("Qualidade do código", "20%", "Nomes claros, funções pequenas, sem repetição, sem código morto"),
            ("Documentação", "15%", "README completo e mensagens de commit que dizem o que mudou"),
            ("Explicação", "15%", "Você explica, em até 5 minutos, qualquer trecho que o professor apontar"),
        ])}
        {usar_ia("explicar erros e conceitos, e revisar o seu código depois de pronto.", "gerar o projeto ou partes inteiras dele. A explicação oral é o que garante isso.")}
      </section>

      <p class="trilha-resumo-texto" id="progresso-geral" data-item="projeto" data-fim="Todos os projetos disponíveis completos. Os próximos saem junto com os módulos."></p>
{etapas_html}
      <p>O Projeto 1 é grátis para todos. Os outros fazem parte da <a href="planos.html">formação</a>, com teste grátis de 7 dias. Os projetos 5 a 12 são liberados junto com os módulos de que dependem, para que você tenha sempre condições de fazê-los sozinho.</p>
    </div>
  </main>

{RODAPE}'''
open(f"{SAIDA}/projetos.html", "w", encoding="utf-8").write(hub)
print("projetos gerados")
