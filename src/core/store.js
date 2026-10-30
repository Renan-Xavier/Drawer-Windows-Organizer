const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');

function parseWindowsRegistryIconSize(rawOutput = '') {
  if (!rawOutput || typeof rawOutput !== 'string') {
    return 'medium';
  }
  const match = rawOutput.match(/IconSize\s+REG_DWORD\s+(0x[0-9a-fA-F]+|\d+)/i);
  if (!match) {
    return 'medium';
  }
  const rawVal = match[1];
  const numeric = rawVal.toLowerCase().startsWith('0x')
    ? parseInt(rawVal, 16)
    : parseInt(rawVal, 10);

  if (Number.isNaN(numeric)) return 'medium';
  if (numeric <= 36) return 'small';
  if (numeric <= 56) return 'medium';
  return 'large';
}

function normalizeDrawerName(name, fallback = 'Nova Gaveta') {
  if (typeof name !== 'string') return fallback;
  const trimmed = name.trim();
  return trimmed.length > 0 ? trimmed.slice(0, 40) : fallback;
}

function clampPosition(
  x = 40,
  y = 40,
  drawerSize = 64,
  screenWidth = 1920,
  screenHeight = 1080
) {
  const margin = 12;
  const labelHeight = 28;
  const maxX = Math.max(margin, screenWidth - drawerSize - margin);
  const maxY = Math.max(margin, screenHeight - drawerSize - labelHeight);

  return {
    x: Math.round(Math.min(Math.max(margin, Number(x) || margin), maxX)),
    y: Math.round(Math.min(Math.max(margin, Number(y) || margin), maxY)),
  };
}

function isInsideDirectory(filePath, parentDir) {
  if (!filePath || !parentDir) return false;
  const rel = path.relative(path.resolve(parentDir), path.resolve(filePath));
  return Boolean(rel && !rel.startsWith('..') && !path.isAbsolute(rel));
}

function moveFileSafe(src, dest) {
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  try {
    fs.renameSync(src, dest);
  } catch {
    fs.copyFileSync(src, dest);
    fs.unlinkSync(src);
  }
}

function createDefaultState() {
  return {
    settings: {
      iconScaleMode: 'auto', // 'auto' | 'small' | 'medium' | 'large'
      backgroundMode: 'blue', // 'blue' | 'transparent'
      launchAtStartup: false, // true | false
      moveDesktopFilesToVault: true, // Default true so icons move into the drawer and back out to Desktop
    },
    drawers: [
      {
        id: 'drawer-default-1',
        name: 'Principais',
        x: 36,
        y: 60,
        preferredDirection: 'auto',
        items: [],
      },
      {
        id: 'drawer-default-2',
        name: 'Jogos & Apps',
        x: 36,
        y: 185,
        preferredDirection: 'auto',
        items: [],
      },
    ],
  };
}

function createDrawerStore({ storageDir, desktopDir }) {
  fs.mkdirSync(storageDir, { recursive: true });
  const stateFilePath = path.join(storageDir, 'drawers-state.json');
  const vaultRootDir = path.join(storageDir, 'vault');
  fs.mkdirSync(vaultRootDir, { recursive: true });

  let state = null;

  function syncVaultWithSettings() {
    if (!state || !desktopDir) return false;
    let changed = false;
    const shouldVault = Boolean(state.settings?.moveDesktopFilesToVault);

    for (const drawer of state.drawers || []) {
      for (const item of drawer.items || []) {
        if (shouldVault) {
          if (
            !item.vaulted &&
            item.targetPath &&
            fs.existsSync(item.targetPath) &&
            isInsideDirectory(item.targetPath, desktopDir)
          ) {
            const itemVaultDir = path.join(vaultRootDir, drawer.id);
            fs.mkdirSync(itemVaultDir, { recursive: true });
            const destPath = path.join(
              itemVaultDir,
              path.basename(item.targetPath)
            );
            const origPath = item.targetPath;
            moveFileSafe(origPath, destPath);
            item.originalDesktopPath = origPath;
            item.targetPath = destPath;
            item.vaulted = true;
            changed = true;
          }
        } else if (item.vaulted && item.targetPath && fs.existsSync(item.targetPath)) {
          const restored = restoreItemFileIfVaulted(item);
          if (restored) {
            item.targetPath = restored;
            item.vaulted = false;
            changed = true;
          }
        }
      }
    }

    if (changed) {
      save();
    }
    return changed;
  }

  function load() {
    if (fs.existsSync(stateFilePath)) {
      try {
        const parsed = JSON.parse(fs.readFileSync(stateFilePath, 'utf8'));
        const mergedSettings = {
          ...createDefaultState().settings,
          ...(parsed.settings || {}),
        };
        mergedSettings.backgroundMode =
          mergedSettings.backgroundMode === 'transparent'
            ? 'transparent'
            : 'blue';
        mergedSettings.launchAtStartup = Boolean(
          mergedSettings.launchAtStartup
        );
        state = {
          settings: mergedSettings,
          drawers: Array.isArray(parsed.drawers) ? parsed.drawers : [],
        };
        syncVaultWithSettings();
        return state;
      } catch {
        // Fallback to default on corrupt file
      }
    }
    state = createDefaultState();
    save();
    return state;
  }

  function save() {
    if (!state) {
      state = createDefaultState();
    }
    fs.mkdirSync(storageDir, { recursive: true });
    fs.writeFileSync(stateFilePath, JSON.stringify(state, null, 2), 'utf8');
    return state;
  }

  function getState() {
    if (!state) load();
    return state;
  }

  function getDrawer(drawerId) {
    return getState().drawers.find((d) => d.id === drawerId) || null;
  }

  function createDrawer({
    name = 'Nova Gaveta',
    x = 48,
    y = 120,
    preferredDirection = 'auto',
  } = {}) {
    const current = getState();
    const drawer = {
      id: `drawer-${crypto.randomUUID().slice(0, 8)}`,
      name: normalizeDrawerName(name),
      x: Number(x) || 48,
      y: Number(y) || 120,
      preferredDirection: ['auto', 'right', 'down', 'left'].includes(
        preferredDirection
      )
        ? preferredDirection
        : 'auto',
      items: [],
    };
    current.drawers.push(drawer);
    save();
    return drawer;
  }

  function renameDrawer(drawerId, newName) {
    const drawer = getDrawer(drawerId);
    if (!drawer) return null;
    drawer.name = normalizeDrawerName(newName);
    save();
    return drawer;
  }

  function moveDrawer(
    drawerId,
    x,
    y,
    drawerSize = 64,
    screenWidth = 1920,
    screenHeight = 1080
  ) {
    const drawer = getDrawer(drawerId);
    if (!drawer) return null;
    const clamped = clampPosition(x, y, drawerSize, screenWidth, screenHeight);
    drawer.x = clamped.x;
    drawer.y = clamped.y;
    save();
    return drawer;
  }

  function setDrawerDirection(drawerId, preferredDirection) {
    const drawer = getDrawer(drawerId);
    if (!drawer) return null;
    if (['auto', 'right', 'down', 'left'].includes(preferredDirection)) {
      drawer.preferredDirection = preferredDirection;
      save();
    }
    return drawer;
  }

  function restoreItemFileIfVaulted(item) {
    if (!item) return null;

    const inVault =
      item.vaulted ||
      (item.targetPath && isInsideDirectory(item.targetPath, vaultRootDir));

    if (inVault && item.targetPath && fs.existsSync(item.targetPath)) {
      let restoreDest =
        item.originalDesktopPath ||
        (desktopDir
          ? path.join(desktopDir, path.basename(item.targetPath))
          : null);

      if (restoreDest) {
        if (fs.existsSync(restoreDest)) {
          // If the exact same file or a shortcut already exists on Desktop, replace/keep it cleanly
          const ext = path.extname(restoreDest).toLowerCase();
          let sameContent = false;
          try {
            const bufA = fs.readFileSync(item.targetPath);
            const bufB = fs.readFileSync(restoreDest);
            sameContent = bufA.equals(bufB);
          } catch {
            sameContent = false;
          }

          if (sameContent || ext === '.lnk' || ext === '.url') {
            try {
              fs.unlinkSync(restoreDest);
            } catch {
              // ignore
            }
          } else {
            const rawExt = path.extname(restoreDest);
            const base = path.basename(restoreDest, rawExt);
            restoreDest = path.join(
              path.dirname(restoreDest),
              `${base} (Restaurado)${rawExt}`
            );
          }
        }
        moveFileSafe(item.targetPath, restoreDest);
        return restoreDest;
      }
    }

    // If not vaulted and user drags item back to Desktop, ensure shortcut exists on Desktop if possible
    if (
      desktopDir &&
      item.targetPath &&
      fs.existsSync(item.targetPath) &&
      !isInsideDirectory(item.targetPath, desktopDir)
    ) {
      const ext = path.extname(item.targetPath).toLowerCase();
      if (ext === '.lnk' || ext === '.url') {
        const destOnDesktop =
          item.originalDesktopPath ||
          path.join(desktopDir, path.basename(item.targetPath));
        if (!fs.existsSync(destOnDesktop)) {
          try {
            fs.mkdirSync(path.dirname(destOnDesktop), { recursive: true });
            fs.copyFileSync(item.targetPath, destOnDesktop);
            return destOnDesktop;
          } catch {
            // fallback
          }
        }
        return destOnDesktop;
      }
    }

    return item.originalDesktopPath || item.targetPath || null;
  }

  function addItemToDrawer(
    drawerId,
    {
      name,
      filePath,
      iconDataUrl = '',
      moveFromDesktop = getState().settings.moveDesktopFilesToVault,
    } = {}
  ) {
    const drawer = getDrawer(drawerId);
    if (!drawer || !filePath) return null;

    // Avoid duplicate path in same drawer
    const existing = drawer.items.find(
      (it) =>
        it.targetPath === filePath || it.originalDesktopPath === filePath
    );
    if (existing) {
      return existing;
    }

    const rawBaseName =
      name ||
      path.basename(filePath, path.extname(filePath)) ||
      path.basename(filePath);

    let finalTargetPath = filePath;
    let originalDesktopPath = null;
    let vaulted = false;

    if (
      moveFromDesktop &&
      desktopDir &&
      fs.existsSync(filePath) &&
      isInsideDirectory(filePath, desktopDir)
    ) {
      const itemVaultDir = path.join(vaultRootDir, drawer.id);
      fs.mkdirSync(itemVaultDir, { recursive: true });
      const destPath = path.join(itemVaultDir, path.basename(filePath));
      moveFileSafe(filePath, destPath);
      finalTargetPath = destPath;
      originalDesktopPath = filePath;
      vaulted = true;
    }

    const item = {
      id: `item-${crypto.randomUUID().slice(0, 8)}`,
      name: normalizeDrawerName(rawBaseName, 'Atalho'),
      targetPath: finalTargetPath,
      originalDesktopPath,
      vaulted,
      iconDataUrl,
    };

    drawer.items.push(item);
    save();
    return item;
  }

  function removeItemFromDrawer(drawerId, itemId) {
    const res = moveItemToDesktop(drawerId, itemId);
    return res.ok;
  }

  function moveItemToDesktop(drawerId, itemId) {
    const drawer = getDrawer(drawerId);
    if (!drawer) return { ok: false, removedItem: null, restoredPath: null };
    const idx = drawer.items.findIndex((it) => it.id === itemId);
    if (idx === -1) return { ok: false, removedItem: null, restoredPath: null };

    const [removed] = drawer.items.splice(idx, 1);
    const restoredPath = restoreItemFileIfVaulted(removed);
    save();
    return {
      ok: true,
      removedItem: removed,
      restoredPath,
    };
  }

  function moveItemBetweenDrawers(fromDrawerId, itemId, toDrawerId) {
    if (!fromDrawerId || !toDrawerId || fromDrawerId === toDrawerId) {
      return null;
    }
    const fromDrawer = getDrawer(fromDrawerId);
    const toDrawer = getDrawer(toDrawerId);
    if (!fromDrawer || !toDrawer) return null;

    const idx = fromDrawer.items.findIndex((it) => it.id === itemId);
    if (idx === -1) return null;

    const [item] = fromDrawer.items.splice(idx, 1);

    // If vaulted, move the vaulted file into the target drawer's vault subfolder
    const inVault =
      item.vaulted ||
      (item.targetPath && isInsideDirectory(item.targetPath, vaultRootDir));
    if (inVault && item.targetPath && fs.existsSync(item.targetPath)) {
      const targetVaultDir = path.join(vaultRootDir, toDrawer.id);
      fs.mkdirSync(targetVaultDir, { recursive: true });
      const newVaultPath = path.join(
        targetVaultDir,
        path.basename(item.targetPath)
      );
      if (path.resolve(item.targetPath) !== path.resolve(newVaultPath)) {
        moveFileSafe(item.targetPath, newVaultPath);
        item.targetPath = newVaultPath;
      }
    }

    const duplicate = toDrawer.items.find(
      (it) =>
        it.targetPath === item.targetPath ||
        (item.originalDesktopPath &&
          it.originalDesktopPath === item.originalDesktopPath)
    );
    if (!duplicate) {
      toDrawer.items.push(item);
    }

    save();
    return duplicate || item;
  }

  function renameItemInDrawer(drawerId, itemId, newName) {
    const drawer = getDrawer(drawerId);
    if (!drawer) return null;
    const item = drawer.items.find((it) => it.id === itemId);
    if (!item) return null;
    item.name = normalizeDrawerName(newName, 'Atalho');
    save();
    return item;
  }

  function deleteDrawer(drawerId) {
    const current = getState();
    const idx = current.drawers.findIndex((d) => d.id === drawerId);
    if (idx === -1) return false;

    const [drawer] = current.drawers.splice(idx, 1);
    for (const item of drawer.items) {
      restoreItemFileIfVaulted(item);
    }
    save();
    return true;
  }

  function setSettings(partial = {}) {
    const current = getState();
    const merged = {
      ...current.settings,
      ...partial,
    };
    merged.backgroundMode =
      merged.backgroundMode === 'transparent' ? 'transparent' : 'blue';
    merged.launchAtStartup = Boolean(merged.launchAtStartup);
    current.settings = merged;
    syncVaultWithSettings();
    save();
    return current.settings;
  }

  return {
    load,
    save,
    getState,
    getDrawer,
    createDrawer,
    renameDrawer,
    moveDrawer,
    setDrawerDirection,
    addItemToDrawer,
    removeItemFromDrawer,
    moveItemToDesktop,
    moveItemBetweenDrawers,
    renameItemInDrawer,
    deleteDrawer,
    setSettings,
  };
}

module.exports = {
  parseWindowsRegistryIconSize,
  normalizeDrawerName,
  clampPosition,
  createDrawerStore,
};
