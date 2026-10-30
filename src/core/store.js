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
      moveDesktopFilesToVault: false, // Default false so dragging creates safe shortcut link unless enabled
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

  function load() {
    if (fs.existsSync(stateFilePath)) {
      try {
        const parsed = JSON.parse(fs.readFileSync(stateFilePath, 'utf8'));
        state = {
          settings: {
            ...createDefaultState().settings,
            ...(parsed.settings || {}),
          },
          drawers: Array.isArray(parsed.drawers) ? parsed.drawers : [],
        };
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
    if (
      item &&
      item.vaulted &&
      item.targetPath &&
      item.originalDesktopPath &&
      fs.existsSync(item.targetPath)
    ) {
      let restoreDest = item.originalDesktopPath;
      if (fs.existsSync(restoreDest)) {
        const ext = path.extname(restoreDest);
        const base = path.basename(restoreDest, ext);
        restoreDest = path.join(
          path.dirname(restoreDest),
          `${base} (Restaurado)${ext}`
        );
      }
      moveFileSafe(item.targetPath, restoreDest);
      return restoreDest;
    }
    return item ? item.targetPath : null;
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
    const drawer = getDrawer(drawerId);
    if (!drawer) return false;
    const idx = drawer.items.findIndex((it) => it.id === itemId);
    if (idx === -1) return false;

    const [removed] = drawer.items.splice(idx, 1);
    restoreItemFileIfVaulted(removed);
    save();
    return true;
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
    current.settings = {
      ...current.settings,
      ...partial,
    };
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
