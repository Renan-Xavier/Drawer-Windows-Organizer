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
const { execFileSync, execFile, spawn } = require('node:child_process');

const {
  createDrawerStore,
  parseWindowsRegistryIconSize,
} = require('../core/store');
const {
  parseUrlShortcutFile,
  getWin32HelperPath,
  extractWindowsIconDataUrl,
} = require('../core/icon-resolver');
const { ensureAppIcons } = require('./icon-generator');

let mainWindow = null;
let settingsWindow = null;
let tray = null;
let store = null;
let appIconPath = null;
let interactiveRects = [];
let forceInteractive = false;
let isCurrentlyIgnoringMouse = null;
let hitTestInterval = null;
let keepBottomCounter = 0;
let desktopWatcherProc = null;

function getWindowHwndDecimal(win) {
  if (!win || win.isDestroyed()) return null;
  try {
    const buf = win.getNativeWindowHandle();
    if (buf.length >= 8) {
      return buf.readBigUInt64LE(0).toString();
    }
    return buf.readUInt32LE(0).toString();
  } catch {
    return null;
  }
}

function pinWindowToDesktopBottom(win) {
  if (process.platform !== 'win32') return;
  const hwndStr = getWindowHwndDecimal(win);
  const helperExe = getWin32HelperPath();
  if (!hwndStr || !fs.existsSync(helperExe)) return;
  try {
    execFileSync(helperExe, ['pin-to-desktop', hwndStr], {
      windowsHide: true,
      timeout: 1500,
    });
  } catch {
    // ignore
  }
}

function keepWindowAtDesktopBottomAsync(win) {
  if (process.platform !== 'win32') return;
  const hwndStr = getWindowHwndDecimal(win);
  const helperExe = getWin32HelperPath();
  if (!hwndStr || !fs.existsSync(helperExe)) return;
  execFile(
    helperExe,
    ['keep-bottom', hwndStr],
    { windowsHide: true, timeout: 1000 },
    () => {}
  );
}

function startDesktopWatcher(win) {
  if (process.platform !== 'win32') return;
  stopDesktopWatcher();
  const hwndStr = getWindowHwndDecimal(win);
  const helperExe = getWin32HelperPath();
  if (!hwndStr || !fs.existsSync(helperExe)) return;
  try {
    desktopWatcherProc = spawn(
      helperExe,
      ['watch-desktop', hwndStr, String(process.pid)],
      {
        windowsHide: true,
        stdio: 'ignore',
      }
    );
    desktopWatcherProc.on('error', () => {
      desktopWatcherProc = null;
    });
    desktopWatcherProc.on('exit', () => {
      desktopWatcherProc = null;
    });
  } catch {
    desktopWatcherProc = null;
  }
}

function stopDesktopWatcher() {
  if (desktopWatcherProc) {
    try {
      desktopWatcherProc.kill();
    } catch {
      // ignore
    }
    desktopWatcherProc = null;
  }
}

function notifyDesktopFileRestored(filePath) {
  if (process.platform !== 'win32' || !filePath) return;
  const helperExe = getWin32HelperPath();
  if (!fs.existsSync(helperExe)) return;
  execFile(
    helperExe,
    ['notify-desktop-file', filePath],
    { windowsHide: true, timeout: 1500 },
    () => {}
  );
}

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
  const payload = getEffectivePayload();
  if (mainWindow && !mainWindow.isDestroyed()) {
    mainWindow.webContents.send('drawer:state-updated', payload);
  }
  if (settingsWindow && !settingsWindow.isDestroyed()) {
    settingsWindow.webContents.send('drawer:state-updated', payload);
  }
  updateTrayMenu();
}

async function extractFileIconDataUrl(filePath) {
  if (!filePath) return '';

  // 1. Primary: Native Windows Shell IShellItemImageFactory + .url IconFile + .lnk resolver
  const shortcutReader = (lnkPath) => {
    try {
      return shell.readShortcutLink(lnkPath);
    } catch {
      return null;
    }
  };

  const highResDataUrl = extractWindowsIconDataUrl(filePath, shortcutReader);
  if (highResDataUrl) {
    return highResDataUrl;
  }

  // 2. Fallback: Electron's app.getFileIcon
  try {
    const img = await app.getFileIcon(filePath, { size: 'large' });
    if (img && !img.isEmpty()) {
      return img.toDataURL();
    }
  } catch {
    // Fallback handled by renderer
  }
  return '';
}

async function refreshAllExistingIcons() {
  if (!store) return;
  const current = store.getState();
  let changed = false;

  for (const drawer of current.drawers || []) {
    for (const item of drawer.items || []) {
      const target = item.targetPath || item.originalDesktopPath;
      if (target && fs.existsSync(target)) {
        const freshIcon = await extractFileIconDataUrl(target);
        if (freshIcon && freshIcon !== item.iconDataUrl) {
          item.iconDataUrl = freshIcon;
          changed = true;
        }
      }
    }
  }

  if (changed) {
    store.save();
    broadcastState();
  }
}

function setupHitTestLoop() {
  if (hitTestInterval) clearInterval(hitTestInterval);

  hitTestInterval = setInterval(() => {
    if (!mainWindow || mainWindow.isDestroyed()) return;

    if (mainWindow.isMinimized()) {
      mainWindow.restore();
      const updated = screen.getPrimaryDisplay().workArea;
      mainWindow.setBounds(updated);
      keepWindowAtDesktopBottomAsync(mainWindow);
    }

    keepBottomCounter = (keepBottomCounter + 1) % 30;
    if (keepBottomCounter === 0) {
      if (!desktopWatcherProc) {
        startDesktopWatcher(mainWindow);
      } else {
        keepWindowAtDesktopBottomAsync(mainWindow);
      }
    }

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
    title: '',
    frame: false,
    transparent: true,
    thickFrame: false,
    roundedCorners: false,
    hasShadow: false,
    focusable: false,
    minimizable: false,
    maximizable: false,
    fullscreenable: false,
    skipTaskbar: true,
    icon: iconPath,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
    },
  });

  mainWindow.setMenuBarVisibility(false);
  mainWindow.loadFile(path.join(__dirname, '..', 'renderer', 'index.html'));

  mainWindow.on('page-title-updated', (e) => {
    e.preventDefault();
  });

  mainWindow.on('minimize', (e) => {
    if (e && typeof e.preventDefault === 'function') {
      e.preventDefault();
    }
    if (!mainWindow || mainWindow.isDestroyed()) return;
    mainWindow.restore();
    const updated = screen.getPrimaryDisplay().workArea;
    mainWindow.setBounds(updated);
    pinWindowToDesktopBottom(mainWindow);
  });

  mainWindow.once('ready-to-show', () => {
    pinWindowToDesktopBottom(mainWindow);
    startDesktopWatcher(mainWindow);
    if (process.env.DRAWER_SMOKE_TEST === '1') {
      setTimeout(() => {
        app.quit();
      }, 1200);
    }
  });

  mainWindow.on('show', () => {
    pinWindowToDesktopBottom(mainWindow);
  });

  mainWindow.on('focus', () => {
    keepWindowAtDesktopBottomAsync(mainWindow);
  });

  screen.on('display-metrics-changed', () => {
    if (!mainWindow || mainWindow.isDestroyed()) return;
    const updated = screen.getPrimaryDisplay().workArea;
    mainWindow.setBounds(updated);
    pinWindowToDesktopBottom(mainWindow);
    broadcastState();
  });

  setupHitTestLoop();
}

function openSettingsWindow() {
  if (settingsWindow && !settingsWindow.isDestroyed()) {
    settingsWindow.show();
    settingsWindow.focus();
    return;
  }

  settingsWindow = new BrowserWindow({
    width: 540,
    height: 540,
    resizable: false,
    minimizable: false,
    maximizable: false,
    fullscreenable: false,
    autoHideMenuBar: true,
    title: 'Configurações do Drawer',
    backgroundColor: '#0f172a',
    icon: appIconPath,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
    },
  });

  settingsWindow.setMenuBarVisibility(false);
  settingsWindow.loadFile(
    path.join(__dirname, '..', 'renderer', 'settings.html')
  );

  settingsWindow.on('closed', () => {
    settingsWindow = null;
    if (mainWindow && !mainWindow.isDestroyed()) {
      pinWindowToDesktopBottom(mainWindow);
    }
  });
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
      label: 'Configurar Gavetas...',
      click: () => {
        openSettingsWindow();
      },
    },
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
    broadcastState();
    return getEffectivePayload();
  });

  ipcMain.handle('drawer:rename', (_event, { drawerId, name }) => {
    store.renameDrawer(drawerId, name);
    broadcastState();
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
    broadcastState();
    return getEffectivePayload();
  });

  ipcMain.handle(
    'drawer:set-direction',
    (_event, { drawerId, preferredDirection }) => {
      store.setDrawerDirection(drawerId, preferredDirection);
      broadcastState();
      return getEffectivePayload();
    }
  );

  ipcMain.handle('drawer:delete', (_event, { drawerId }) => {
    store.deleteDrawer(drawerId);
    notifyDesktopFileRestored(app.getPath('desktop'));
    broadcastState();
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
    broadcastState();
    return getEffectivePayload();
  });

  ipcMain.handle('drawer:pick-files', async (_event, { drawerId }) => {
    forceInteractive = true;
    if (mainWindow && !mainWindow.isDestroyed()) {
      mainWindow.setIgnoreMouseEvents(false);
    }
    try {
      const result = await dialog.showOpenDialog({
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
      if (mainWindow && !mainWindow.isDestroyed()) {
        pinWindowToDesktopBottom(mainWindow);
      }
    }
    broadcastState();
    return getEffectivePayload();
  });

  ipcMain.handle('drawer:remove-item', (_event, { drawerId, itemId }) => {
    const res = store.moveItemToDesktop(drawerId, itemId);
    if (res && res.restoredPath) {
      notifyDesktopFileRestored(res.restoredPath);
    }
    broadcastState();
    return getEffectivePayload();
  });

  ipcMain.handle(
    'drawer:move-item-to-desktop',
    (_event, { drawerId, itemId }) => {
      const res = store.moveItemToDesktop(drawerId, itemId);
      if (res && res.restoredPath) {
        notifyDesktopFileRestored(res.restoredPath);
      }
      broadcastState();
      return getEffectivePayload();
    }
  );

  ipcMain.handle(
    'drawer:move-item-between-drawers',
    (_event, { fromDrawerId, itemId, toDrawerId }) => {
      store.moveItemBetweenDrawers(fromDrawerId, itemId, toDrawerId);
      broadcastState();
      return getEffectivePayload();
    }
  );

  ipcMain.handle('drawer:rename-item', (_event, { drawerId, itemId, name }) => {
    store.renameItemInDrawer(drawerId, itemId, name);
    broadcastState();
    return getEffectivePayload();
  });

  ipcMain.handle('drawer:open-item', async (_event, { targetPath }) => {
    if (!targetPath || !fs.existsSync(targetPath)) {
      return { ok: false, error: 'Arquivo ou atalho não encontrado.' };
    }
    if (targetPath.toLowerCase().endsWith('.url')) {
      try {
        const content = fs.readFileSync(targetPath, 'utf8');
        const parsed = parseUrlShortcutFile(content);
        if (parsed.url) {
          await shell.openExternal(parsed.url);
          return { ok: true, error: null };
        }
      } catch {
        // Fallback to shell.openPath
      }
    }
    const errMessage = await shell.openPath(targetPath);
    return { ok: !errMessage, error: errMessage || null };
  });

  ipcMain.handle('drawer:update-settings', (_event, partialSettings) => {
    store.setSettings(partialSettings);
    broadcastState();
    return getEffectivePayload();
  });

  ipcMain.handle('drawer:refresh-win-scale', () => {
    return getEffectivePayload();
  });

  ipcMain.handle('drawer:open-settings', () => {
    openSettingsWindow();
    return { ok: true };
  });

  ipcMain.handle('drawer:close-settings', () => {
    if (settingsWindow && !settingsWindow.isDestroyed()) {
      settingsWindow.close();
    }
    return { ok: true };
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
  app.whenReady().then(async () => {
    const assetsDir = path.join(app.getPath('userData'), 'icons');
    const { pngPath, icoPath } = ensureAppIcons(assetsDir);
    appIconPath = process.platform === 'win32' ? icoPath : pngPath;

    store = createDrawerStore({
      storageDir: app.getPath('userData'),
      desktopDir: app.getPath('desktop'),
    });
    store.load();

    registerIpcHandlers();
    createOverlayWindow(appIconPath);

    const trayImg = nativeImage
      .createFromPath(pngPath)
      .resize({ width: 16, height: 16 });
    tray = new Tray(trayImg);
    tray.setToolTip('Drawer — Organizador de Ícones');
    tray.on('double-click', () => openSettingsWindow());
    updateTrayMenu();

    // Atualiza automaticamente os ícones já salvos (ex.: atalhos .url da Steam)
    await refreshAllExistingIcons();
  });

  app.on('before-quit', () => {
    if (hitTestInterval) clearInterval(hitTestInterval);
    stopDesktopWatcher();
  });
}
