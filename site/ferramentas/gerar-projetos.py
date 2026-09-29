"""
Gera de novo a página de projetos (projetos.html) e as páginas projeto-N.html.

Use quando:
  - colocar o vídeo ou as capturas do resultado esperado em midia-projetos/
  - mudar o texto de um projeto ou liberar um projeto novo (em ferramentas/projetos/gerar_projetos.py:
    na lista MAPA, troque False por True no projeto e escreva o conteúdo dele)

Como rodar, dentro da pasta do site:
    python ferramentas/gerar-projetos.py
"""
import os, runpy, sys
if sys.version_info < (3, 12):
    sys.exit("Este gerador precisa do Python 3.12 ou mais novo (python --version mostra o seu).")
aqui = os.path.join(os.path.dirname(os.path.abspath(__file__)), "projetos")
sys.path.insert(0, aqui)
runpy.run_path(os.path.join(aqui, "gerar_projetos.py"), run_name="__main__")
runpy.run_path(os.path.join(aqui, "aplicar_plataforma.py"), run_name="__main__")
print("Pronto. Se o site roda no servidor, copie a pasta do site para o servidor (veja o LEIA-ME da plataforma).")
