const test = require('node:test');
const assert = require('node:assert/strict');

const {
  SNAP_THRESHOLD_PX,
  getScaleMetrics,
  computeDrawerGrid,
  computeOpenDirection,
  computeMagneticDock,
  computeDrawerLayout,
  createInteractionMachine,
} = require('../src/core/geometry');

test('getScaleMetrics returns Windows icon scale dimensions for small, medium, and large', () => {
  const small = getScaleMetrics('small');
  const medium = getScaleMetrics('medium');
  const large = getScaleMetrics('large');
  const fallback = getScaleMetrics('invalid');

  assert.equal(small.iconSize, 32);
  assert.equal(small.drawerSize, 48);
  assert.equal(medium.iconSize, 48);
  assert.equal(medium.drawerSize, 64);
  assert.equal(large.iconSize, 64);
  assert.equal(large.drawerSize, 88);
  assert.deepEqual(fallback, medium);
});

test('computeDrawerGrid auto-adjusts rectangular dimensions tightly to icon count and direction', () => {
  const g0 = computeDrawerGrid(0, 'right', 'medium');
  const g1 = computeDrawerGrid(1, 'right', 'medium');
  const g2 = computeDrawerGrid(2, 'right', 'medium');
  const g3 = computeDrawerGrid(3, 'right', 'medium');
  const g4 = computeDrawerGrid(4, 'right', 'medium');
  const g5 = computeDrawerGrid(5, 'right', 'medium');

  assert.equal(g0.cols, 1);
  assert.equal(g0.rows, 1);
  assert.equal(g1.cols, 1);
  assert.equal(g1.rows, 1);
  assert.equal(g2.cols, 2);
  assert.equal(g2.rows, 1);
  assert.equal(g3.cols, 3);
  assert.equal(g3.rows, 1);
  assert.equal(g4.cols, 4);
  assert.equal(g4.rows, 1);
  assert.equal(g5.cols, 4);
  assert.equal(g5.rows, 2);

  assert.ok(g2.width > g1.width, '2 icons must be wider than 1 icon');
  assert.ok(g3.width > g2.width, '3 icons must be wider than 2 icons');
  assert.ok(g5.height > g4.height, '5 icons must add a second row');

  const down2 = computeDrawerGrid(2, 'down', 'medium');
  const down5 = computeDrawerGrid(5, 'down', 'medium');
  assert.equal(down2.cols, 1);
  assert.equal(down2.rows, 2);
  assert.equal(down5.cols, 2);
  assert.equal(down5.rows, 4);
});

test('computeOpenDirection chooses right, down, or left based on screen bounds', () => {
  // Left side of screen -> opens right
  const leftEdge = computeOpenDirection({
    x: 24,
    y: 120,
    iconCount: 4,
    scale: 'medium',
    screenWidth: 1920,
    screenHeight: 1080,
    preferredDirection: 'auto',
  });
  assert.equal(leftEdge, 'right');

  // Right edge of screen -> opens left (or down if preferred)
  const rightEdge = computeOpenDirection({
    x: 1840,
    y: 200,
    iconCount: 4,
    scale: 'medium',
    screenWidth: 1920,
    screenHeight: 1080,
    preferredDirection: 'auto',
  });
  assert.equal(rightEdge, 'left');

  // Explicit preference 'down' when space below is available
  const preferDown = computeOpenDirection({
    x: 40,
    y: 60,
    iconCount: 3,
    scale: 'medium',
    screenWidth: 1920,
    screenHeight: 1080,
    preferredDirection: 'down',
  });
  assert.equal(preferDown, 'down');

  // Explicit preference 'right' near right screen edge falls back safely
  const overflowRight = computeOpenDirection({
    x: 1850,
    y: 100,
    iconCount: 4,
    scale: 'medium',
    screenWidth: 1920,
    screenHeight: 1080,
    preferredDirection: 'right',
  });
  assert.notEqual(overflowRight, 'right');
});

test('createInteractionMachine holds open for 1250ms after mouseLeave and cancels on re-entry', async (t) => {
  t.mock.timers.enable({ apis: ['setTimeout'] });

  const states = [];
  const machine = createInteractionMachine({
    hoverDelayMs: 1250,
    onChange: (s) => states.push(s),
  });

  assert.deepEqual(machine.getState(), {
    isOpen: false,
    isPinned: false,
    state: 'closed',
  });

  machine.mouseEnter();
  assert.equal(machine.getState().isOpen, true);
  assert.equal(machine.getState().state, 'hover-open');

  machine.mouseLeave();
  assert.equal(machine.getState().isOpen, true);
  assert.equal(machine.getState().state, 'hover-closing');

  // After 1000ms (less than 1250ms), still open!
  t.mock.timers.tick(1000);
  assert.equal(machine.getState().isOpen, true);

  // Re-enter at 1000ms -> cancels close countdown
  machine.mouseEnter();
  assert.equal(machine.getState().state, 'hover-open');
  t.mock.timers.tick(1000);
  assert.equal(machine.getState().isOpen, true);

  // Leave again and wait full 1250ms -> closes
  machine.mouseLeave();
  t.mock.timers.tick(1250);
  assert.deepEqual(machine.getState(), {
    isOpen: false,
    isPinned: false,
    state: 'closed',
  });
});

test('createInteractionMachine pins open on first click and closes on second click', (t) => {
  t.mock.timers.enable({ apis: ['setTimeout'] });

  const machine = createInteractionMachine({ hoverDelayMs: 1250 });

  // User hovers first, then clicks -> must pin open, NOT close!
  machine.mouseEnter();
  machine.toggleClick();
  assert.deepEqual(machine.getState(), {
    isOpen: true,
    isPinned: true,
    state: 'pinned',
  });

  // Mouse leaves while pinned -> stays open even after 5 seconds
  machine.mouseLeave();
  t.mock.timers.tick(5000);
  assert.equal(machine.getState().isOpen, true);
  assert.equal(machine.getState().isPinned, true);

  // Second click -> unpins and closes immediately
  machine.toggleClick();
  assert.deepEqual(machine.getState(), {
    isOpen: false,
    isPinned: false,
    state: 'closed',
  });
});

test('computeMagneticDock snaps Pod to left, right, or top screen edges within SNAP_THRESHOLD_PX (2px) and leaves y=12 free', () => {
  assert.equal(SNAP_THRESHOLD_PX, 2);

  const leftSnap = computeMagneticDock({
    x: 1,
    y: 220,
    drawerSize: 64,
    screenWidth: 1920,
    screenHeight: 1080,
  });
  assert.equal(leftSnap.dockedEdge, 'left');
  assert.equal(leftSnap.x, 0);
  assert.equal(leftSnap.y, 220);
  assert.equal(leftSnap.forcedDirection, 'right');

  const rightSnap = computeMagneticDock({
    x: 1920 - 64 - 1,
    y: 300,
    drawerSize: 64,
    screenWidth: 1920,
    screenHeight: 1080,
  });
  assert.equal(rightSnap.dockedEdge, 'right');
  assert.equal(rightSnap.x, 1920 - 64);
  assert.equal(rightSnap.y, 300);
  assert.equal(rightSnap.forcedDirection, 'left');

  const topSnap = computeMagneticDock({
    x: 500,
    y: 1,
    drawerSize: 64,
    screenWidth: 1920,
    screenHeight: 1080,
  });
  assert.equal(topSnap.dockedEdge, 'top');
  assert.equal(topSnap.x, 500);
  assert.equal(topSnap.y, 0);
  assert.equal(topSnap.forcedDirection, 'down');

  const nearTopRowDesktop = computeMagneticDock({
    x: 500,
    y: 12,
    drawerSize: 64,
    screenWidth: 1920,
    screenHeight: 1080,
  });
  assert.equal(nearTopRowDesktop.dockedEdge, 'none');
  assert.equal(nearTopRowDesktop.x, 500);
  assert.equal(nearTopRowDesktop.y, 12);

  const freeDesktop = computeMagneticDock({
    x: 240,
    y: 240,
    drawerSize: 64,
    screenWidth: 1920,
    screenHeight: 1080,
  });
  assert.equal(freeDesktop.dockedEdge, 'none');
  assert.equal(freeDesktop.x, 240);
  assert.equal(freeDesktop.y, 240);
  assert.equal(freeDesktop.forcedDirection, null);
});

test('computeDrawerLayout reflects magnetic edge docking and forces inward opening direction', () => {
  const layoutLeft = computeDrawerLayout(
    {
      id: 'd-left',
      name: 'Borda Esquerda',
      x: 1,
      y: 180,
      preferredDirection: 'left',
      items: [{ id: '1' }, { id: '2' }],
    },
    { isOpen: true, scale: 'medium', screenWidth: 1920, screenHeight: 1080 }
  );
  assert.equal(layoutLeft.dockedEdge, 'left');
  assert.equal(layoutLeft.direction, 'right');

  const layoutRight = computeDrawerLayout(
    {
      id: 'd-right',
      name: 'Borda Direita',
      x: 1920 - 64 - 1,
      y: 180,
      preferredDirection: 'right',
      items: [{ id: '1' }, { id: '2' }],
    },
    { isOpen: true, scale: 'medium', screenWidth: 1920, screenHeight: 1080 }
  );
  assert.equal(layoutRight.dockedEdge, 'right');
  assert.equal(layoutRight.direction, 'left');
});
