(function () {
  const scaleSelect = document.getElementById('scale-select');
  const bgModeSelect = document.getElementById('bg-mode-select');
  const startupSelect = document.getElementById('startup-select');
  const vaultCheck = document.getElementById('vault-check');
  const btnAddDrawer = document.getElementById('btn-add-drawer');
  const drawerListEl = document.getElementById('drawer-list');
  const btnOk = document.getElementById('btn-ok');

  let currentState = null;

  function renderDrawers(state) {
    currentState = state;
    if (!state) return;

    scaleSelect.value = state.settings?.iconScaleMode || 'auto';
    if (bgModeSelect) {
      bgModeSelect.value =
        state.settings?.backgroundMode === 'transparent'
          ? 'transparent'
          : 'blue';
    }
    if (startupSelect) {
      startupSelect.value = state.settings?.launchAtStartup
        ? 'enabled'
        : 'disabled';
    }
    vaultCheck.checked = Boolean(state.settings?.moveDesktopFilesToVault);

    drawerListEl.innerHTML = '';
    for (const drawer of state.drawers || []) {
      const row = document.createElement('div');
      row.className = 'drawer-row';

      const nameInput = document.createElement('input');
      nameInput.type = 'text';
      nameInput.value = drawer.name;
      nameInput.maxLength = 40;
      nameInput.dataset.drawerId = drawer.id;
      nameInput.addEventListener('change', async () => {
        const next = await window.drawerAPI.renameDrawer(
          drawer.id,
          nameInput.value
        );
        renderDrawers(next);
      });

      const dirSelect = document.createElement('select');
      const dirs = [
        { value: 'auto', label: 'Direção: Auto' },
        { value: 'right', label: 'Para a Direita' },
        { value: 'down', label: 'Para Baixo' },
        { value: 'left', label: 'Para a Esquerda' },
      ];
      for (const d of dirs) {
        const opt = document.createElement('option');
        opt.value = d.value;
        opt.textContent = d.label;
        dirSelect.appendChild(opt);
      }
      dirSelect.value = drawer.preferredDirection || 'auto';
      dirSelect.addEventListener('change', async () => {
        const next = await window.drawerAPI.setDrawerDirection(
          drawer.id,
          dirSelect.value
        );
        renderDrawers(next);
      });

      const delBtn = document.createElement('button');
      delBtn.type = 'button';
      delBtn.className = 'btn danger';
      delBtn.textContent = 'Excluir';
      delBtn.addEventListener('click', async () => {
        const next = await window.drawerAPI.deleteDrawer(drawer.id);
        renderDrawers(next);
      });

      row.appendChild(nameInput);
      row.appendChild(dirSelect);
      row.appendChild(delBtn);
      drawerListEl.appendChild(row);
    }
  }

  scaleSelect.addEventListener('change', async () => {
    const next = await window.drawerAPI.updateSettings({
      iconScaleMode: scaleSelect.value,
    });
    renderDrawers(next);
  });

  if (bgModeSelect) {
    bgModeSelect.addEventListener('change', async () => {
      const next = await window.drawerAPI.updateSettings({
        backgroundMode: bgModeSelect.value,
      });
      renderDrawers(next);
    });
  }

  if (startupSelect) {
    startupSelect.addEventListener('change', async () => {
      const next = await window.drawerAPI.updateSettings({
        launchAtStartup: startupSelect.value === 'enabled',
      });
      renderDrawers(next);
    });
  }

  vaultCheck.addEventListener('change', async () => {
    const next = await window.drawerAPI.updateSettings({
      moveDesktopFilesToVault: vaultCheck.checked,
    });
    renderDrawers(next);
  });

  btnAddDrawer.addEventListener('click', async () => {
    const next = await window.drawerAPI.createDrawer({});
    renderDrawers(next);
    const inputs = drawerListEl.querySelectorAll('input[type="text"]');
    if (inputs.length > 0) {
      const last = inputs[inputs.length - 1];
      last.focus();
      last.select();
    }
  });

  btnOk.addEventListener('click', async () => {
    // Save any edited drawer names before closing
    const inputs = drawerListEl.querySelectorAll('input[type="text"]');
    for (const inp of inputs) {
      const id = inp.dataset.drawerId;
      const existing = currentState?.drawers?.find((d) => d.id === id);
      if (existing && existing.name !== inp.value.trim() && inp.value.trim()) {
        await window.drawerAPI.renameDrawer(id, inp.value);
      }
    }
    await window.drawerAPI.updateSettings({
      iconScaleMode: scaleSelect.value,
      backgroundMode: bgModeSelect ? bgModeSelect.value : 'blue',
      launchAtStartup: startupSelect
        ? startupSelect.value === 'enabled'
        : false,
      moveDesktopFilesToVault: vaultCheck.checked,
    });
    window.drawerAPI.closeSettingsWindow();
  });

  if (window.drawerAPI) {
    window.drawerAPI.getInitialState().then(renderDrawers);
    window.drawerAPI.onStateChanged(renderDrawers);
  }
})();
