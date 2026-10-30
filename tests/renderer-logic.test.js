const test = require('node:test');
const assert = require('node:assert/strict');

const {
  computeDrawerLayout,
  findDrawerAtPoint,
} = require('../src/core/geometry');

test('computeDrawerLayout computes Discord-style 2x2 preview, connected panel bounds, and hit-test rects', () => {
  const drawer = {
    id: 'd1',
    name: 'Faculdade',
    x: 40,
    y: 100,
    preferredDirection: 'auto',
    items: [
      { id: '1', name: 'VS Code' },
      { id: '2', name: 'Discord' },
      { id: '3', name: 'Chrome' },
      { id: '4', name: 'Git' },
      { id: '5', name: 'Node' },
    ],
  };

  const closedLayout = computeDrawerLayout(drawer, {
    isOpen: false,
    isPinned: false,
    scale: 'medium',
    screenWidth: 1920,
    screenHeight: 1080,
  });

  // Preview shows at most 4 items (2x2 Discord folder grid)
  assert.equal(closedLayout.previewItems.length, 4);
  assert.equal(closedLayout.direction, 'right');
  assert.equal(closedLayout.grid.cols, 4);
  assert.equal(closedLayout.grid.rows, 2);

  // When closed, interactiveRect covers the closed drawer + label only
  assert.equal(closedLayout.interactiveRect.width, 84);

  const openLayout = computeDrawerLayout(drawer, {
    isOpen: true,
    isPinned: true,
    scale: 'medium',
    screenWidth: 1920,
    screenHeight: 1080,
  });

  // When open to the right, interactiveRect expands to cover both the drawer AND the sliding rectangular panel
  assert.ok(
    openLayout.interactiveRect.width > closedLayout.interactiveRect.width + 200
  );
  assert.equal(openLayout.isPinned, true);

  // Panel must NEVER overlap the drawer handle in any direction
  const handleRight = drawer.x + openLayout.metrics.drawerSize;
  assert.ok(
    openLayout.panelRect.x >= handleRight + 6,
    'Right-opening panel must start to the right of the drawer handle without overlapping'
  );

  const downLayout = computeDrawerLayout(
    { ...drawer, preferredDirection: 'down' },
    { isOpen: true, isPinned: false, scale: 'small', screenWidth: 1920, screenHeight: 1080 }
  );
  const handleBottomWithLabel = drawer.y + downLayout.metrics.drawerSize + 26;
  assert.ok(
    downLayout.panelRect.y >= handleBottomWithLabel,
    'Down-opening panel must start below the drawer handle and label without overlapping'
  );
});

test('findDrawerAtPoint detects whether drop point is inside open drawer panel, another drawer handle, or empty desktop', () => {
  const drawers = [
    {
      id: 'd1',
      name: 'Principais',
      x: 40,
      y: 100,
      preferredDirection: 'right',
      items: [{ id: 'i1', name: 'App 1' }, { id: 'i2', name: 'App 2' }],
    },
    {
      id: 'd2',
      name: 'Jogos',
      x: 40,
      y: 300,
      preferredDirection: 'right',
      items: [],
    },
  ];

  const openStates = {
    d1: { isOpen: true, isPinned: false },
    d2: { isOpen: false, isPinned: false },
  };

  // Point inside d1's open panel (to the right of x=40+64+8=112)
  assert.equal(
    findDrawerAtPoint(drawers, 140, 130, {
      openStates,
      scale: 'medium',
      screenWidth: 1920,
      screenHeight: 1080,
    }),
    'd1'
  );

  // Point inside d2's closed handle (x=40..104, y=300..364)
  assert.equal(
    findDrawerAtPoint(drawers, 60, 320, {
      openStates,
      scale: 'medium',
      screenWidth: 1920,
      screenHeight: 1080,
    }),
    'd2'
  );

  // Point on empty Desktop (x=600, y=500) -> returns null so item is restored to Desktop
  assert.equal(
    findDrawerAtPoint(drawers, 600, 500, {
      openStates,
      scale: 'medium',
      screenWidth: 1920,
      screenHeight: 1080,
    }),
    null
  );
});

test('settings UI and stylesheet support blue and 100% transparent (no-blur, lines-only) background mode', () => {
  const fs = require('node:fs');
  const path = require('node:path');

  const settingsHtml = fs.readFileSync(
    path.join(__dirname, '..', 'src', 'renderer', 'settings.html'),
    'utf8'
  );
  const stylesCss = fs.readFileSync(
    path.join(__dirname, '..', 'src', 'renderer', 'styles.css'),
    'utf8'
  );
  const appJs = fs.readFileSync(
    path.join(__dirname, '..', 'src', 'renderer', 'app.js'),
    'utf8'
  );

  // Settings window has select #bg-mode-select with 'blue' and 'transparent' options (no slider)
  assert.match(settingsHtml, /id="bg-mode-select"/);
  assert.match(settingsHtml, /<option value="blue">/);
  assert.match(settingsHtml, /<option value="transparent">/);
  assert.doesNotMatch(settingsHtml, /type="range"/);

  // Stylesheet defines .theme-transparent with transparent background and no blur
  assert.match(stylesCss, /\.theme-transparent\s+\.drawer-handle/);
  assert.match(stylesCss, /\.theme-transparent\s+\.drawer-panel/);
  assert.match(stylesCss, /backdrop-filter:\s*none/);

  // Renderer applies theme-transparent based on backgroundMode
  assert.match(appJs, /theme-transparent/);
});

test('Win32Helper and mainWindow strip WS_CAPTION and WS_SYSMENU so top white titlebar never appears', () => {
  const fs = require('node:fs');
  const path = require('node:path');

  const helperCs = fs.readFileSync(
    path.join(__dirname, '..', 'src', 'native', 'Win32Helper.cs'),
    'utf8'
  );
  const mainJs = fs.readFileSync(
    path.join(__dirname, '..', 'src', 'main', 'main.js'),
    'utf8'
  );

  // EnsureOnDesktop must strip WS_CAPTION and WS_SYSMENU from GWL_STYLE and set WS_POPUP
  assert.match(helperCs, /WS_CAPTION/);
  assert.match(helperCs, /WS_SYSMENU/);
  assert.match(helperCs, /SetWindowLongPtr\(hwnd,\s*GWL_STYLE/);
  assert.doesNotMatch(helperCs, /SetWindowText\(hwnd/);

  // mainWindow must disable closable so WS_SYSMENU close button is never created on the desktop overlay
  assert.match(mainJs, /closable:\s*false/);
  assert.match(mainJs, /OVERLAY_TOP_GUARD_PX/);
  assert.match(mainJs, /setLoginItemSettings/);
});

test('settings UI includes launchAtStartup (Inicializar com o Windows) option with enabled and disabled choices', () => {
  const fs = require('node:fs');
  const path = require('node:path');

  const settingsHtml = fs.readFileSync(
    path.join(__dirname, '..', 'src', 'renderer', 'settings.html'),
    'utf8'
  );
  const settingsJs = fs.readFileSync(
    path.join(__dirname, '..', 'src', 'renderer', 'settings.js'),
    'utf8'
  );

  assert.match(settingsHtml, /id="startup-select"/);
  assert.match(settingsHtml, /<option value="enabled">Ativado<\/option>/);
  assert.match(settingsHtml, /<option value="disabled">Desativado<\/option>/);
  assert.match(settingsJs, /launchAtStartup/);
});




