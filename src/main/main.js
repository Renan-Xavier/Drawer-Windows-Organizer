const {
  app,
  BrowserWindow,
  ipcMain,
  Tray,
  Menu,
  screen,
  shell,
  dialog,
  nativeImage,
} = require('electron');
const path = require('node:path');
const fs = require('node:fs');
const { execFileSync } = require('node:child_process');

const {
  createDrawerStore,
  parseWindowsRegistryIconSize,
} = require('../core/store');
const { ensureAppIcons } = require('./icon-generator');

let mainWindow = null;
let tray = null;
let store = null;
let interactiveRects = [];
let forceInteractive = false;
let isCurrentlyIgnoringMouse = null;
let hitTestInterval = null;

function readWindowsDesktopIconScale() {
  if (process.platform !== 'win32') return 'medium';
  try {
    const output = execFileSync(
      'reg.exe',
      [
        'query',
        'HKCU\\Software\\Microsoft\\Windows\\Shell\\Bags\\1\\Desktop',
        '/v',
        'IconSize',
      ],
      { encoding: 'utf8', windowsHide: true, timeout: 1500 }
    );
    return parseWindowsRegistryIconSize(output);
  } catch {
    return 'medium';
  }
}

function getEffectivePayload() {
  const rawState = store.getState();
  const windowsScale = readWindowsDesktopIconScale();
  const effectiveScale =
    rawState.settings.iconScaleMode === 'auto'
      ? windowsScale
      : rawState.settings.iconScaleMode;

  const display = screen.getPrimaryDisplay();
  const { width, height } = display.workAreaSize;

  return {
    ...rawState,
    windowsScale,
    effectiveScale,
    screenWidth: width,
    screenHeight: height,
  };
}

function broadcastState() {
  if (mainWindow && !mainWindow.isDestroyed()) {
    mainWindow.webContents.send('drawer:state-updated', getEffectivePayload());
  }
  updateTrayMenu();
}

async function extractFileIconDataUrl(filePath) {
  try {
    let iconTarget = filePath;
    if (
      process.platform === 'win32' &&
      filePath.toLowerCase().endsWith('.lnk')
    ) {
      try {
        const shortcut = shell.readShortcutLink(filePath);
        if (shortcut && shortcut.icon && fs.existsSync(shortcut.icon)) {
          iconTarget = shortcut.icon;
        } else if (
          shortcut &&
          shortcut.target &&
          fs.existsSync(shortcut.target)
        ) {
          iconTarget = shortcut.target;
        }
      } catch {
        // Fallback to .lnk itself
      }
    }
    const img = await app.getFileIcon(iconTarget, { size: 'large' });
    if (img && !img.isEmpty()) {
      return img.toDataURL();
    }
  } catch {
    // Fallback handled by renderer
  }
  return '';
}

function setupHitTestLoop() {
  if (hitTestInterval) clearInterval(hitTestInterval);

  hitTestInterval = setInterval(() => {
    if (!mainWindow || mainWindow.isDestroyed()) return;

    if (forceInteractive) {
      if (isCurrentlyIgnoringMouse !== false) {
        isCurrentlyIgnoringMouse = false;
        mainWindow.setIgnoreMouseEvents(false);
      }
      return;
    }

    const cursor = screen.getCursorScreenPoint();
    const bounds = mainWindow.getBounds();
    const localX = cursor.x - bounds.x;
    const localY = cursor.y - bounds.y;

    // Expand hit zone by 14px around each drawer so OLE Drag & Drop and hover
    // activate cleanly as the cursor approaches any drawer.
    const pad = 14;
    const hit = interactiveRects.some(
      (r) =>
        localX >= r.x - pad &&
        localX <= r.x + r.width + pad &&
        localY >= r.y - pad &&
        localY <= r.y + r.height + pad
    );

    const shouldIgnore = !hit;
    if (shouldIgnore !== isCurrentlyIgnoringMouse) {
      isCurrentlyIgnoringMouse = shouldIgnore;
      if (shouldIgnore) {
        mainWindow.setIgnoreMouseEvents(true, { forward: true });
      } else {
        mainWindow.setIgnoreMouseEvents(false);
      }
    }
  }, 35);
}

function createOverlayWindow(iconPath) {
  const display = screen.getPrimaryDisplay();
  const { x, y, width, height } = display.workArea;

  mainWindow = new BrowserWindow({
    x,
    y,
    width,
    height,
    frame: false,
    transparent: true,
    resizable: false,
    movable: false,
    minimizable: false,
    maximizable: false,
    fullscreenable: false,
    skipTaskbar: true,
    hasShadow: false,
    icon: iconPath,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
    },
  });

  mainWindow.setMenuBarVisibility(false);
  mainWindow.loadFile(path.join(__dirname, '..', 'renderer', 'index.html'));

  mainWindow.once('ready-to-show', () => {
    if (process.env.DRAWER_SMOKE_TEST === '1') {
      setTimeout(() => {
        app.quit();
      }, 1200);
    }
  });

  screen.on('display-metrics-changed', () => {
    if (!mainWindow || mainWindow.isDestroyed()) return;
    const updated = screen.getPrimaryDisplay().workArea;
    mainWindow.setBounds(updated);
    broadcastState();
  });

  setupHitTestLoop();
}

function updateTrayMenu() {
  if (!tray || !store) return;
  const current = store.getState();
  const mode = current.settings.iconScaleMode;
  const vault = Boolean(current.settings.moveDesktopFilesToVault);

  const contextMenu = Menu.buildFromTemplate([
    {
      label: 'Drawer — Organizador de Gavetas',
      enabled: false,
    },
    { type: 'separator' },
    {
      label: '+ Nova Gaveta',
      click: () => {
        const count = current.drawers.length;
        store.createDrawer({
          name: `Gaveta ${count + 1}`,
          x: 40,
          y: 60 + count * 120,
        });
        broadcastState();
      },
    },
    { type: 'separator' },
    {
      label: 'Tamanho dos Ícones',
      submenu: [
        {
          label: 'Sincronizar com o Windows (Automático)',
          type: 'radio',
          checked: mode === 'auto',
          click: () => {
            store.setSettings({ iconScaleMode: 'auto' });
            broadcastState();
          },
        },
        {
          label: 'Pequenos (Windows Small)',
          type: 'radio',
          checked: mode === 'small',
          click: () => {
            store.setSettings({ iconScaleMode: 'small' });
            broadcastState();
          },
        },
        {
          label: 'Médios (Windows Medium)',
          type: 'radio',
          checked: mode === 'medium',
          click: () => {
            store.setSettings({ iconScaleMode: 'medium' });
            broadcastState();
          },
        },
        {
          label: 'Grandes (Windows Large)',
          type: 'radio',
          checked: mode === 'large',
          click: () => {
            store.setSettings({ iconScaleMode: 'large' });
            broadcastState();
          },
        },
      ],
    },
    {
      label: 'Mover arquivo da Área de Trabalho ao arrastar',
      type: 'checkbox',
      checked: vault,
      click: (menuItem) => {
        store.setSettings({ moveDesktopFilesToVault: menuItem.checked });
        broadcastState();
      },
    },
    { type: 'separator' },
    {
      label: 'Sair do Drawer',
      click: () => {
        app.quit();
      },
    },
  ]);

  tray.setContextMenu(contextMenu);
}

function registerIpcHandlers() {
  ipcMain.handle('drawer:get-initial-state', () => {
    return getEffectivePayload();
  });

  ipcMain.handle('drawer:create', (_event, payload = {}) => {
    const current = store.getState();
    const count = current.drawers.length;
    store.createDrawer({
      name: payload.name || `Gaveta ${count + 1}`,
      x: payload.x ?? 40,
      y: payload.y ?? Math.min(800, 60 + count * 120),
      preferredDirection: payload.preferredDirection || 'auto',
    });
    const next = getEffectivePayload();
    updateTrayMenu();
    return next;
  });

  ipcMain.handle('drawer:rename', (_event, { drawerId, name }) => {
    store.renameDrawer(drawerId, name);
    return getEffectivePayload();
  });

  ipcMain.handle('drawer:move', (_event, { drawerId, x, y, drawerSize }) => {
    const display = screen.getPrimaryDisplay();
    store.moveDrawer(
      drawerId,
      x,
      y,
      drawerSize,
      display.workAreaSize.width,
      display.workAreaSize.height
    );
    return getEffectivePayload();
  });

  ipcMain.handle(
    'drawer:set-direction',
    (_event, { drawerId, preferredDirection }) => {
      store.setDrawerDirection(drawerId, preferredDirection);
      return getEffectivePayload();
    }
  );

  ipcMain.handle('drawer:delete', (_event, { drawerId }) => {
    store.deleteDrawer(drawerId);
    updateTrayMenu();
    return getEffectivePayload();
  });

  ipcMain.handle('drawer:add-files', async (_event, { drawerId, filePaths }) => {
    if (Array.isArray(filePaths)) {
      for (const fp of filePaths) {
        if (!fp || typeof fp !== 'string') continue;
        const iconDataUrl = await extractFileIconDataUrl(fp);
        store.addItemToDrawer(drawerId, {
          filePath: fp,
          iconDataUrl,
        });
      }
    }
    return getEffectivePayload();
  });

  ipcMain.handle('drawer:pick-files', async (_event, { drawerId }) => {
    forceInteractive = true;
    if (mainWindow && !mainWindow.isDestroyed()) {
      mainWindow.setIgnoreMouseEvents(false);
    }
    try {
      const result = await dialog.showOpenDialog(mainWindow, {
        title: 'Adicionar ícones/atalhos à Gaveta',
        defaultPath: app.getPath('desktop'),
        properties: ['openFile', 'multiSelections', 'dontAddToRecent'],
      });
      if (!result.canceled && Array.isArray(result.filePaths)) {
        for (const fp of result.filePaths) {
          const iconDataUrl = await extractFileIconDataUrl(fp);
          store.addItemToDrawer(drawerId, {
            filePath: fp,
            iconDataUrl,
          });
        }
      }
    } finally {
      forceInteractive = false;
    }
    return getEffectivePayload();
  });

  ipcMain.handle('drawer:remove-item', (_event, { drawerId, itemId }) => {
    store.removeItemFromDrawer(drawerId, itemId);
    return getEffectivePayload();
  });

  ipcMain.handle('drawer:rename-item', (_event, { drawerId, itemId, name }) => {
    store.renameItemInDrawer(drawerId, itemId, name);
    return getEffectivePayload();
  });

  ipcMain.handle('drawer:open-item', async (_event, { targetPath }) => {
    if (!targetPath || !fs.existsSync(targetPath)) {
      return { ok: false, error: 'Arquivo ou atalho não encontrado.' };
    }
    const errMessage = await shell.openPath(targetPath);
    return { ok: !errMessage, error: errMessage || null };
  });

  ipcMain.handle('drawer:update-settings', (_event, partialSettings) => {
    store.setSettings(partialSettings);
    updateTrayMenu();
    return getEffectivePayload();
  });

  ipcMain.handle('drawer:refresh-win-scale', () => {
    return getEffectivePayload();
  });

  ipcMain.on('drawer:set-interactive-rects', (_event, rects) => {
    if (Array.isArray(rects)) {
      interactiveRects = rects;
    }
  });

  ipcMain.on('drawer:set-force-interactive', (_event, force) => {
    forceInteractive = Boolean(force);
    if (mainWindow && !mainWindow.isDestroyed()) {
      if (forceInteractive) {
        isCurrentlyIgnoringMouse = false;
        mainWindow.setIgnoreMouseEvents(false);
      }
    }
  });
}

const gotTheLock = app.requestSingleInstanceLock();
if (!gotTheLock) {
  app.quit();
} else {
  app.whenReady().then(() => {
    const assetsDir = path.join(app.getPath('userData'), 'icons');
    const { pngPath, icoPath } = ensureAppIcons(assetsDir);

    store = createDrawerStore({
      storageDir: app.getPath('userData'),
      desktopDir: app.getPath('desktop'),
    });
    store.load();

    registerIpcHandlers();
    createOverlayWindow(process.platform === 'win32' ? icoPath : pngPath);

    const trayImg = nativeImage
      .createFromPath(pngPath)
      .resize({ width: 16, height: 16 });
    tray = new Tray(trayImg);
    tray.setToolTip('Drawer — Organizador de Ícones');
    updateTrayMenu();
  });

  app.on('before-quit', () => {
    if (hitTestInterval) clearInterval(hitTestInterval);
  });
}
