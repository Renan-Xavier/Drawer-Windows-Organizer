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
});
