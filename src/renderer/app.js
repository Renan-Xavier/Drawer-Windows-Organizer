/* global DrawerGeometry */
(function () {
  const { computeDrawerLayout, createInteractionMachine } = DrawerGeometry;

  const canvas = document.getElementById('desktop-canvas');
  const contextMenu = document.getElementById('context-menu');
  const toastEl = document.getElementById('toast');

  let appState = {
    settings: { iconScaleMode: 'auto', moveDesktopFilesToVault: false },
    effectiveScale: 'medium',
    screenWidth: window.innerWidth,
    screenHeight: window.innerHeight,
    drawers: [],
  };

  // Maps drawer.id -> { rootEl, handleEl, previewEl, labelWrapEl, panelEl, itemsGridEl, machine }
  const drawerNodes = new Map();
  let toastTimer = null;

  function showToast(msg) {
    if (!toastEl) return;
    toastEl.textContent = msg;
    toastEl.classList.remove('hidden');
    if (toastTimer) clearTimeout(toastTimer);
    toastTimer = setTimeout(() => {
      toastEl.classList.add('hidden');
    }, 2500);
  }

  function syncInteractiveRects() {
    if (!window.drawerAPI) return;
    const rects = [];

    if (contextMenu && !contextMenu.classList.contains('hidden')) {
      const r = contextMenu.getBoundingClientRect();
      rects.push({ x: r.left, y: r.top, width: r.width, height: r.height });
    }

    for (const drawer of appState.drawers) {
      const entry = drawerNodes.get(drawer.id);
      const mState = entry
        ? entry.machine.getState()
        : { isOpen: false, isPinned: false };
      const layout = computeDrawerLayout(drawer, {
        isOpen: mState.isOpen,
        isPinned: mState.isPinned,
        scale: appState.effectiveScale || 'medium',
        screenWidth: appState.screenWidth || window.innerWidth,
        screenHeight: appState.screenHeight || window.innerHeight,
      });
      rects.push(layout.interactiveRect);
    }

    window.drawerAPI.setInteractiveRects(rects);
  }

  function closeContextMenu() {
    if (!contextMenu.classList.contains('hidden')) {
      contextMenu.classList.add('hidden');
      contextMenu.innerHTML = '';
      window.drawerAPI.setForceInteractive(false);
      syncInteractiveRects();
    }
  }

  function openContextMenu(x, y, items) {
    contextMenu.innerHTML = '';
    for (const item of items) {
      if (item.separator) {
        const sep = document.createElement('div');
        sep.className = 'ctx-sep';
        contextMenu.appendChild(sep);
        continue;
      }
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = `ctx-item${item.danger ? ' danger' : ''}`;
      btn.textContent = item.label;
      btn.addEventListener('click', (e) => {
        e.stopPropagation();
        closeContextMenu();
        item.onClick();
      });
      contextMenu.appendChild(btn);
    }

    contextMenu.classList.remove('hidden');
    const rect = contextMenu.getBoundingClientRect();
    const clampedX = Math.min(x, window.innerWidth - rect.width - 12);
    const clampedY = Math.min(y, window.innerHeight - rect.height - 12);
    contextMenu.style.left = `${Math.max(8, clampedX)}px`;
    contextMenu.style.top = `${Math.max(8, clampedY)}px`;

    window.drawerAPI.setForceInteractive(true);
    syncInteractiveRects();
  }

  function renderPreviewSlots(previewEl, previewItems) {
    previewEl.innerHTML = '';
    for (let i = 0; i < 4; i++) {
      const slot = document.createElement('div');
      slot.className = 'preview-slot';
      const item = previewItems[i];
      if (item) {
        if (item.iconDataUrl) {
          const img = document.createElement('img');
          img.src = item.iconDataUrl;
          img.alt = '';
          slot.appendChild(img);
        } else {
          const span = document.createElement('span');
          span.className = 'preview-fallback-glyph';
          span.textContent = (item.name || '?').slice(0, 1).toUpperCase();
          slot.appendChild(span);
        }
      }
      previewEl.appendChild(slot);
    }
  }

  function renderPanelItems(itemsGridEl, drawer) {
    itemsGridEl.innerHTML = '';
    const items = Array.isArray(drawer.items) ? drawer.items : [];

    if (items.length === 0) {
      const emptySlot = document.createElement('div');
      emptySlot.className = 'drawer-empty-slot';
      emptySlot.innerHTML = `
        <span class="empty-plus">+</span>
        <span class="empty-hint">Arraste aqui</span>
      `;
      emptySlot.addEventListener('click', async (e) => {
        e.stopPropagation();
        const nextState = await window.drawerAPI.pickFilesForDrawer(drawer.id);
        applyState(nextState);
      });
      itemsGridEl.appendChild(emptySlot);
      return;
    }

    for (const item of items) {
      const cell = document.createElement('div');
      cell.className = 'drawer-item';

      if (item.iconDataUrl) {
        const img = document.createElement('img');
        img.className = 'drawer-item-icon';
        img.src = item.iconDataUrl;
        img.alt = '';
        cell.appendChild(img);
      } else {
        const fb = document.createElement('div');
        fb.className = 'drawer-item-fallback';
        fb.textContent = (item.name || '?').slice(0, 1).toUpperCase();
        cell.appendChild(fb);
      }

      const nameSpan = document.createElement('span');
      nameSpan.className = 'drawer-item-name';
      nameSpan.textContent = item.name;
      cell.appendChild(nameSpan);

      cell.addEventListener('click', async (e) => {
        e.stopPropagation();
        const res = await window.drawerAPI.openItem(item.targetPath);
        if (!res || !res.ok) {
          showToast(res?.error || 'Não foi possível abrir este atalho.');
        }
      });

      cell.addEventListener('contextmenu', (e) => {
        e.preventDefault();
        e.stopPropagation();
        openContextMenu(e.clientX, e.clientY, [
          {
            label: `Abrir "${item.name}"`,
            onClick: () => window.drawerAPI.openItem(item.targetPath),
          },
          {
            label: 'Remover da Gaveta',
            danger: true,
            onClick: async () => {
              const nextState = await window.drawerAPI.removeItemFromDrawer(
                drawer.id,
                item.id
              );
              applyState(nextState);
            },
          },
        ]);
      });

      itemsGridEl.appendChild(cell);
    }
  }

  function createDrawerDOM(drawer) {
    const rootEl = document.createElement('div');
    rootEl.className = 'drawer-unit dir-right';
    rootEl.dataset.drawerId = drawer.id;

    const handleEl = document.createElement('div');
    handleEl.className = 'drawer-handle';

    const previewEl = document.createElement('div');
    previewEl.className = 'drawer-preview-grid';
    handleEl.appendChild(previewEl);

    const pinBadge = document.createElement('div');
    pinBadge.className = 'pin-badge';
    handleEl.appendChild(pinBadge);

    const labelWrapEl = document.createElement('div');
    labelWrapEl.className = 'drawer-label-wrap';

    const panelEl = document.createElement('div');
    panelEl.className = 'drawer-panel';

    const itemsGridEl = document.createElement('div');
    itemsGridEl.className = 'drawer-items-grid';
    panelEl.appendChild(itemsGridEl);

    rootEl.appendChild(handleEl);
    rootEl.appendChild(labelWrapEl);
    rootEl.appendChild(panelEl);

    const machine = createInteractionMachine({
      hoverDelayMs: 1250,
      onChange: (mState) => {
        rootEl.classList.toggle('is-open', mState.isOpen);
        rootEl.classList.toggle('is-pinned', mState.isPinned);
        syncInteractiveRects();
      },
    });

    rootEl.addEventListener('mouseenter', () => {
      machine.mouseEnter();
    });

    rootEl.addEventListener('mouseleave', () => {
      machine.mouseLeave();
    });

    handleEl.addEventListener('mousedown', (e) => {
      if (e.button !== 0) return;
      e.stopPropagation();
      closeContextMenu();

      const currentDrawer =
        appState.drawers.find((d) => d.id === drawer.id) || drawer;
      const startMouseX = e.clientX;
      const startMouseY = e.clientY;
      const startDrawerX = currentDrawer.x;
      const startDrawerY = currentDrawer.y;
      let moved = false;

      window.drawerAPI.setForceInteractive(true);

      const onMouseMove = (moveEvent) => {
        const dx = moveEvent.clientX - startMouseX;
        const dy = moveEvent.clientY - startMouseY;
        if (!moved && Math.hypot(dx, dy) > 5) {
          moved = true;
          rootEl.classList.add('is-dragging');
        }
        if (moved) {
          currentDrawer.x = Math.round(startDrawerX + dx);
          currentDrawer.y = Math.round(startDrawerY + dy);
          updateSingleDrawerDOM(currentDrawer);
        }
      };

      const onMouseUp = async () => {
        window.removeEventListener('mousemove', onMouseMove);
        window.removeEventListener('mouseup', onMouseUp);
        rootEl.classList.remove('is-dragging');
        window.drawerAPI.setForceInteractive(false);

        if (moved) {
          const layout = computeDrawerLayout(currentDrawer, {
            scale: appState.effectiveScale,
            screenWidth: window.innerWidth,
            screenHeight: window.innerHeight,
          });
          const nextState = await window.drawerAPI.moveDrawer(
            currentDrawer.id,
            currentDrawer.x,
            currentDrawer.y,
            layout.metrics.drawerSize
          );
          applyState(nextState);
        } else {
          machine.toggleClick();
        }
      };

      window.addEventListener('mousemove', onMouseMove);
      window.addEventListener('mouseup', onMouseUp);
    });

    handleEl.addEventListener('contextmenu', (e) => {
      e.preventDefault();
      e.stopPropagation();
      const currentDrawer =
        appState.drawers.find((d) => d.id === drawer.id) || drawer;
      const mState = machine.getState();
      const pref = currentDrawer.preferredDirection || 'auto';

      openContextMenu(e.clientX, e.clientY, [
        {
          label: mState.isPinned ? 'Desafixar e Fechar Gaveta' : 'Fixar Gaveta Aberta',
          onClick: () => machine.toggleClick(),
        },
        {
          label: 'Configurar Gavetas...',
          onClick: () => window.drawerAPI.openSettingsWindow(),
        },
        {
          label: '+ Adicionar Ícones / Atalhos...',
          onClick: async () => {
            machine.mouseEnter();
            const nextState = await window.drawerAPI.pickFilesForDrawer(
              currentDrawer.id
            );
            applyState(nextState);
          },
        },
        { separator: true },
        {
          label: `${pref === 'auto' ? '✓ ' : ''}Abrir: Automático (Inteligente)`,
          onClick: async () => {
            const nextState = await window.drawerAPI.setDrawerDirection(
              currentDrawer.id,
              'auto'
            );
            applyState(nextState);
          },
        },
        {
          label: `${pref === 'right' ? '✓ ' : ''}Abrir para a Direita`,
          onClick: async () => {
            const nextState = await window.drawerAPI.setDrawerDirection(
              currentDrawer.id,
              'right'
            );
            applyState(nextState);
          },
        },
        {
          label: `${pref === 'down' ? '✓ ' : ''}Abrir para Baixo`,
          onClick: async () => {
            const nextState = await window.drawerAPI.setDrawerDirection(
              currentDrawer.id,
              'down'
            );
            applyState(nextState);
          },
        },
        {
          label: `${pref === 'left' ? '✓ ' : ''}Abrir para a Esquerda`,
          onClick: async () => {
            const nextState = await window.drawerAPI.setDrawerDirection(
              currentDrawer.id,
              'left'
            );
            applyState(nextState);
          },
        },
        { separator: true },
        {
          label: 'Excluir Gaveta',
          danger: true,
          onClick: async () => {
            const nextState = await window.drawerAPI.deleteDrawer(
              currentDrawer.id
            );
            applyState(nextState);
          },
        },
      ]);
    });

    const onDragOver = (e) => {
      e.preventDefault();
      e.stopPropagation();
      if (e.dataTransfer) {
        e.dataTransfer.dropEffect = 'copy';
      }
      rootEl.classList.add('drag-target');
      machine.mouseEnter();
    };

    const onDragLeave = (e) => {
      e.preventDefault();
      e.stopPropagation();
      if (!rootEl.contains(e.relatedTarget)) {
        rootEl.classList.remove('drag-target');
      }
    };

    const onDrop = async (e) => {
      e.preventDefault();
      e.stopPropagation();
      rootEl.classList.remove('drag-target');

      const files = Array.from(e.dataTransfer?.files || []);
      const filePaths = files
        .map((f) => window.drawerAPI.getPathForFile(f))
        .filter(Boolean);

      if (filePaths.length > 0) {
        machine.mouseEnter();
        const nextState = await window.drawerAPI.addFilesToDrawer(
          drawer.id,
          filePaths
        );
        applyState(nextState);
      }
    };

    rootEl.addEventListener('dragenter', onDragOver);
    rootEl.addEventListener('dragover', onDragOver);
    rootEl.addEventListener('dragleave', onDragLeave);
    rootEl.addEventListener('drop', onDrop);

    canvas.appendChild(rootEl);

    const entry = {
      rootEl,
      handleEl,
      previewEl,
      labelWrapEl,
      panelEl,
      itemsGridEl,
      machine,
    };
    drawerNodes.set(drawer.id, entry);
    return entry;
  }

  function updateSingleDrawerDOM(drawer) {
    let entry = drawerNodes.get(drawer.id);
    if (!entry) {
      entry = createDrawerDOM(drawer);
    }

    const mState = entry.machine.getState();
    const layout = computeDrawerLayout(drawer, {
      isOpen: mState.isOpen,
      isPinned: mState.isPinned,
      scale: appState.effectiveScale || 'medium',
      screenWidth: appState.screenWidth || window.innerWidth,
      screenHeight: appState.screenHeight || window.innerHeight,
    });

    const { rootEl, previewEl, labelWrapEl, itemsGridEl } = entry;
    const { metrics, grid, direction, previewItems } = layout;

    rootEl.style.left = `${drawer.x}px`;
    rootEl.style.top = `${drawer.y}px`;
    rootEl.style.setProperty('--drawer-size', `${metrics.drawerSize}px`);
    rootEl.style.setProperty('--drawer-radius', `${metrics.borderRadius}px`);
    rootEl.style.setProperty(
      '--preview-icon-size',
      `${metrics.previewIconSize}px`
    );
    rootEl.style.setProperty('--icon-size', `${metrics.iconSize}px`);
    rootEl.style.setProperty('--cell-width', `${metrics.cellWidth}px`);
    rootEl.style.setProperty('--cell-height', `${metrics.cellHeight}px`);
    rootEl.style.setProperty('--panel-padding', `${metrics.padding}px`);
    rootEl.style.setProperty('--grid-gap', `${metrics.gap}px`);
    rootEl.style.setProperty('--grid-cols', String(grid.cols));
    rootEl.style.setProperty('--grid-rows', String(grid.rows));
    rootEl.style.setProperty('--panel-width', `${grid.width}px`);
    rootEl.style.setProperty('--panel-height', `${grid.height}px`);

    rootEl.classList.remove('dir-right', 'dir-left', 'dir-down');
    rootEl.classList.add(`dir-${direction}`);
    rootEl.classList.toggle('is-open', mState.isOpen);
    rootEl.classList.toggle('is-pinned', mState.isPinned);

    renderPreviewSlots(previewEl, previewItems);

    labelWrapEl.innerHTML = '';
    const labelSpan = document.createElement('span');
    labelSpan.className = 'drawer-label';
    labelSpan.textContent = drawer.name;
    labelSpan.addEventListener('dblclick', (e) => {
      e.stopPropagation();
      window.drawerAPI.openSettingsWindow();
    });
    labelWrapEl.appendChild(labelSpan);

    renderPanelItems(itemsGridEl, drawer);
  }

  function renderAllDrawers() {
    const activeIds = new Set(appState.drawers.map((d) => d.id));

    for (const [id, entry] of drawerNodes.entries()) {
      if (!activeIds.has(id)) {
        entry.machine.destroy();
        entry.rootEl.remove();
        drawerNodes.delete(id);
      }
    }

    for (const drawer of appState.drawers) {
      updateSingleDrawerDOM(drawer);
    }

    syncInteractiveRects();
  }

  function applyState(nextState) {
    if (!nextState) return;
    appState = {
      ...appState,
      ...nextState,
      screenWidth: nextState.screenWidth || window.innerWidth,
      screenHeight: nextState.screenHeight || window.innerHeight,
    };
    renderAllDrawers();
  }

  window.addEventListener('click', () => {
    closeContextMenu();
  });

  window.addEventListener('resize', () => {
    appState.screenWidth = window.innerWidth;
    appState.screenHeight = window.innerHeight;
    renderAllDrawers();
  });

  if (window.drawerAPI) {
    window.drawerAPI.getInitialState().then((initial) => {
      applyState(initial);
    });

    window.drawerAPI.onStateChanged((updated) => {
      applyState(updated);
    });
  }
})();
