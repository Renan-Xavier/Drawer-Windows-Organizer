const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');

const {
  parseWindowsRegistryIconSize,
  normalizeDrawerName,
  clampPosition,
  createDrawerStore,
} = require('../src/core/store');

test('parseWindowsRegistryIconSize maps Windows Explorer registry hex/decimal values to small, medium, large', () => {
  assert.equal(
    parseWindowsRegistryIconSize('    IconSize    REG_DWORD    0x20'),
    'small'
  );
  assert.equal(
    parseWindowsRegistryIconSize('    IconSize    REG_DWORD    0x30'),
    'medium'
  );
  assert.equal(
    parseWindowsRegistryIconSize('    IconSize    REG_DWORD    0x60'),
    'large'
  );
  assert.equal(parseWindowsRegistryIconSize(''), 'medium');
});

test('normalizeDrawerName trims valid names and falls back on empty or whitespace names', () => {
  assert.equal(normalizeDrawerName('  Jogos  '), 'Jogos');
  assert.equal(normalizeDrawerName('   '), 'Nova Gaveta');
  assert.equal(normalizeDrawerName(''), 'Nova Gaveta');
  assert.equal(normalizeDrawerName(null), 'Nova Gaveta');
});

test('clampPosition keeps drawer coordinates inside screen boundaries', () => {
  const pos1 = clampPosition(-50, -20, 64, 1920, 1080);
  assert.equal(pos1.x, 12);
  assert.equal(pos1.y, 12);

  const pos2 = clampPosition(2500, 1400, 64, 1920, 1080);
  assert.ok(pos2.x <= 1920 - 64 - 12);
  assert.ok(pos2.y <= 1080 - 64 - 28);
});

test('createDrawerStore manages drawers, persists state, and safely vaults/restores desktop files', () => {
  const tmpRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'drawer-test-'));
  const storageDir = path.join(tmpRoot, 'appdata');
  const desktopDir = path.join(tmpRoot, 'Desktop');
  fs.mkdirSync(desktopDir, { recursive: true });

  const store = createDrawerStore({ storageDir, desktopDir });
  const initialState = store.load();
  assert.ok(Array.isArray(initialState.drawers));
  assert.equal(initialState.drawers.length, 2);

  // Create a new named drawer
  const created = store.createDrawer({ name: 'Faculdade FIAP', x: 80, y: 300 });
  assert.equal(created.name, 'Faculdade FIAP');

  // Rename with empty string falls back to 'Nova Gaveta'
  store.renameDrawer(created.id, '   ');
  assert.equal(store.getDrawer(created.id).name, 'Nova Gaveta');
  store.renameDrawer(created.id, 'Projetos');
  assert.equal(store.getDrawer(created.id).name, 'Projetos');

  // Create a dummy shortcut file on the Desktop and drag it into the drawer with vaulting enabled
  const desktopFile = path.join(desktopDir, 'Discord.lnk');
  fs.writeFileSync(desktopFile, 'shortcut-binary-content', 'utf8');

  const addedItem = store.addItemToDrawer(created.id, {
    name: 'Discord',
    filePath: desktopFile,
    iconDataUrl: 'data:image/png;base64,AAA',
    moveFromDesktop: true,
  });

  // File should now be moved out of Desktop into the drawer's vault
  assert.equal(fs.existsSync(desktopFile), false);
  assert.equal(fs.existsSync(addedItem.targetPath), true);

  // Removing the item restores it back to Desktop
  store.removeItemFromDrawer(created.id, addedItem.id);
  assert.equal(fs.existsSync(desktopFile), true);
  assert.equal(fs.readFileSync(desktopFile, 'utf8'), 'shortcut-binary-content');

  // Re-add and then delete the entire drawer -> must also restore the file to Desktop
  store.addItemToDrawer(created.id, {
    name: 'Discord',
    filePath: desktopFile,
    iconDataUrl: 'data:image/png;base64,AAA',
    moveFromDesktop: true,
  });
  assert.equal(fs.existsSync(desktopFile), false);

  store.deleteDrawer(created.id);
  assert.equal(fs.existsSync(desktopFile), true);

  fs.rmSync(tmpRoot, { recursive: true, force: true });
});
