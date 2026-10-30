# Drawer — Organizador de Ícones para Windows

Organizador de ícones para a Área de Trabalho do Windows baseado em **Gavetas** (*Drawers*), inspirado nas pastas de servidores do **Discord**.

## Principais Recursos

- **Gavetas Azuis de Pontas Arredondadas:** Visual moderno em azul (diferente da pasta amarela tradicional do Windows Explorer) com prévia interna 2×2 estilo Discord.
- **Abertura Contínua com Autoajuste Geométrico:** Ao abrir, expande lateralmente (ou para baixo) como um quadrado/retângulo de pontas arredondadas conectado à própria gaveta, ajustando sua largura e altura exatamente à quantidade de ícones dentro dela (`1×1`, `2×1`, `3×1`, `4×1`, `4×2`, etc.).
- **Direção Inteligente:** Reconhece a posição na tela e abre automaticamente para a **direita**, **baixo** ou **esquerda**.
- **Animação e Fade Suaves:** Movimento e opacidade com curva suave (`260ms cubic-bezier(0.22, 1, 0.36, 1)`).
- **Interação Hover + Clique:**
  - **Passar o mouse (*Hover*):** Abre suavemente e permanece aberta por **1,25 segundo** (`1250ms`) mesmo após retirar o mouse.
  - **1º Clique:** Fixa a gaveta aberta permanentemente.
  - **2º Clique:** Desafixa e fecha a gaveta.
- **Nomeação e Classificação:** Dê duplo clique no nome da gaveta (ou use o menu de botão direito → *Renomear Gaveta*) para nomeá-la livremente.
- **Tamanho Sincronizado com o Windows:** Sincroniza automaticamente com a escala de ícones da Área de Trabalho do Windows (*Pequeno*, *Médio* ou *Grande*) ou permite escolha manual.
- **Drag & Drop:** Arraste atalhos, programas ou arquivos da Área de Trabalho diretamente para dentro da gaveta.

## Comandos

```bash
# Executar testes automatizados
npm test

# Iniciar em modo de desenvolvimento
npm start

# Gerar o Instalador Windows (.exe) na pasta dist/
npm run dist
```
