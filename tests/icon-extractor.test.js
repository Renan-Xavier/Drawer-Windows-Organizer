const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');

const {
  parseUrlShortcutFile,
  resolveIconSourcePath,
  extractWindowsIconDataUrl,
} = require('../src/core/icon-resolver');

test('parseUrlShortcutFile extracts IconFile, IconIndex, and URL from Steam/Windows .url files', () => {
  const content = [
    '[{000214A0-0000-0000-C000-000000000046}]',
    'Prop3=19,0',
    '[InternetShortcut]',
    'IDList=',
    'IconIndex=0',
    'URL=steam://rungameid/1245620',
    'IconFile=C:\\Program Files (x86)\\Steam\\steam\\games\\2e048bfc2073ca30804ed5b8c454a9ca0e2f98de.ico',
  ].join('\r\n');

  const parsed = parseUrlShortcutFile(content);
  assert.equal(
    parsed.iconFile,
    'C:\\Program Files (x86)\\Steam\\steam\\games\\2e048bfc2073ca30804ed5b8c454a9ca0e2f98de.ico'
  );
  assert.equal(parsed.iconIndex, 0);
  assert.equal(parsed.url, 'steam://rungameid/1245620');
});

test('resolveIconSourcePath resolves .url IconFile when it exists on disk', () => {
  const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'drawer-url-test-'));
  const icoPath = path.join(tmpDir, 'game.ico');
  const urlPath = path.join(tmpDir, 'MyGame.url');

  fs.writeFileSync(icoPath, 'dummy-ico-bytes');
  fs.writeFileSync(
    urlPath,
    `[InternetShortcut]\r\nURL=steam://rungameid/123\r\nIconFile=${icoPath}\r\nIconIndex=0\r\n`,
    'utf8'
  );

  const resolved = resolveIconSourcePath(urlPath);
  assert.equal(resolved.iconPath, icoPath);
  assert.equal(resolved.kind, 'ico');

  fs.rmSync(tmpDir, { recursive: true, force: true });
});

test('extractWindowsIconDataUrl extracts real PNG data URL from .url pointing to a valid .ico file', () => {
  const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'drawer-real-ico-'));
  const realIcoPath = path.join(__dirname, '..', 'assets', 'icon.ico');
  const urlPath = path.join(tmpDir, 'SteamGame.url');

  fs.writeFileSync(
    urlPath,
    `[InternetShortcut]\r\nURL=steam://rungameid/999\r\nIconFile=${realIcoPath}\r\nIconIndex=0\r\n`,
    'utf8'
  );

  const dataUrl = extractWindowsIconDataUrl(urlPath);
  assert.ok(
    dataUrl.startsWith('data:image/png;base64,'),
    'Must return a valid PNG data URL'
  );
  assert.ok(dataUrl.length > 500, 'PNG data URL must contain real icon data');

  fs.rmSync(tmpDir, { recursive: true, force: true });
});

