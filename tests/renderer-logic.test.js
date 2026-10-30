const test = require('node:test');
const assert = require('node:assert/strict');

const { computeDrawerLayout } = require('../src/core/geometry');

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
});
