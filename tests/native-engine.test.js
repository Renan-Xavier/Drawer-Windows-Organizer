const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');

const distDrawerExe = path.join(__dirname, '..', 'dist', 'Drawer.exe');

test('Native C#/WPF Drawer.exe compiles and passes internal --self-test suite (geometry, magnetic dock, startup registry, shortcut resolution, icon extraction)', () => {
  assert.equal(
    fs.existsSync(distDrawerExe),
    true,
    'dist/Drawer.exe must be compiled and present'
  );

  const output = execFileSync(distDrawerExe, ['--self-test'], {
    encoding: 'utf8',
    windowsHide: true,
    timeout: 15000,
  }).trim();

  const report = JSON.parse(output);
  assert.equal(report.ok, true, `Self-test failed: ${output}`);
  assert.equal(report.geometryOk, true);
  assert.equal(report.magneticDockOk, true);
  assert.equal(report.startupRegistryOk, true);
  assert.equal(report.shortcutResolverOk, true);
  assert.equal(report.iconExtractionOk, true);
  assert.equal(report.catalogScannerOk, true);
  assert.equal(report.labelSizingOk, true, 'Drawer and icon labels must be wide enough not to cut off names like Ferramentas or Emuladores');
  assert.equal(report.noLabelPanelOverlapOk, true, 'Open panel must never overlap the drawer name pill badge');
  assert.equal(report.compactPanelPaddingOk, true, 'Open panel must use tight padding and cell height without dead vertical space');
  assert.equal(report.desktopIconPositionerOk, true, 'Dragging an icon to Desktop must position it at the cursor drop coordinates');
});

test('Native C#/WPF Drawer.exe passes --smoke-test creating independent frameless WPF Pod windows without errors', () => {
  assert.equal(fs.existsSync(distDrawerExe), true);

  const output = execFileSync(distDrawerExe, ['--smoke-test'], {
    encoding: 'utf8',
    windowsHide: true,
    timeout: 15000,
  }).trim();

  const report = JSON.parse(output);
  assert.equal(report.ok, true, `Smoke test failed: ${output}`);
  assert.ok(report.podsCreated >= 1, 'At least 1 native WPF Pod window must be created');
  assert.equal(report.hasTitleBar, false, 'Native WPF Pod must NEVER have a title bar');
  assert.equal(report.isFullScreenOverlay, false, 'Native WPF Pod must NOT be a fullscreen overlay');
  assert.equal(report.desktopLayerBottomLocked, true, 'Desktop Pods must lock to HWND_BOTTOM and return MA_NOACTIVATE so they never overlay Windows apps');
  assert.equal(report.usesWin32TrackPopupMenu, true, 'Pod context menu must use Win32 TrackPopupMenuEx so clicking outside always closes the menu');
});
