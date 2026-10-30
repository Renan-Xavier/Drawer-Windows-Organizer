const { contextBridge, ipcRenderer, webUtils } = require('electron');

contextBridge.exposeInMainWorld('drawerAPI', {
  getInitialState: () => ipcRenderer.invoke('drawer:get-initial-state'),
  createDrawer: (payload) => ipcRenderer.invoke('drawer:create', payload),
  renameDrawer: (drawerId, name) =>
    ipcRenderer.invoke('drawer:rename', { drawerId, name }),
  moveDrawer: (drawerId, x, y, drawerSize) =>
    ipcRenderer.invoke('drawer:move', { drawerId, x, y, drawerSize }),
  setDrawerDirection: (drawerId, preferredDirection) =>
    ipcRenderer.invoke('drawer:set-direction', {
      drawerId,
      preferredDirection,
    }),
  deleteDrawer: (drawerId) =>
    ipcRenderer.invoke('drawer:delete', { drawerId }),
  addFilesToDrawer: (drawerId, filePaths) =>
    ipcRenderer.invoke('drawer:add-files', { drawerId, filePaths }),
  pickFilesForDrawer: (drawerId) =>
    ipcRenderer.invoke('drawer:pick-files', { drawerId }),
  removeItemFromDrawer: (drawerId, itemId) =>
    ipcRenderer.invoke('drawer:remove-item', { drawerId, itemId }),
  moveItemToDesktop: (drawerId, itemId) =>
    ipcRenderer.invoke('drawer:move-item-to-desktop', { drawerId, itemId }),
  moveItemBetweenDrawers: (fromDrawerId, itemId, toDrawerId) =>
    ipcRenderer.invoke('drawer:move-item-between-drawers', {
      fromDrawerId,
      itemId,
      toDrawerId,
    }),
  renameItemInDrawer: (drawerId, itemId, name) =>
    ipcRenderer.invoke('drawer:rename-item', { drawerId, itemId, name }),
  openItem: (targetPath) =>
    ipcRenderer.invoke('drawer:open-item', { targetPath }),
  updateSettings: (settings) =>
    ipcRenderer.invoke('drawer:update-settings', settings),
  refreshWindowsIconScale: () =>
    ipcRenderer.invoke('drawer:refresh-win-scale'),
  openSettingsWindow: () => ipcRenderer.invoke('drawer:open-settings'),
  closeSettingsWindow: () => ipcRenderer.invoke('drawer:close-settings'),
  setInteractiveRects: (rects) =>
    ipcRenderer.send('drawer:set-interactive-rects', rects),
  setForceInteractive: (force) =>
    ipcRenderer.send('drawer:set-force-interactive', Boolean(force)),
  getPathForFile: (file) => {
    try {
      if (webUtils && typeof webUtils.getPathForFile === 'function') {
        const resolved = webUtils.getPathForFile(file);
        if (resolved) return resolved;
      }
    } catch {
      // ignore
    }
    return file && file.path ? file.path : '';
  },
  onStateChanged: (callback) => {
    const handler = (_event, newState) => callback(newState);
    ipcRenderer.on('drawer:state-updated', handler);
    return () => ipcRenderer.removeListener('drawer:state-updated', handler);
  },
});
