const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');

const {
  generateDrawerPngBuffer,
  generateDrawerIcoBuffer,
  ensureAppIcons,
} = require('../src/main/icon-generator');

test('generateDrawerPngBuffer produces a valid 256x256 PNG buffer', () => {
  const png = generateDrawerPngBuffer(256);
  assert.ok(Buffer.isBuffer(png));
  // PNG magic bytes: 89 50 4E 47 0D 0A 1A 0A
  assert.equal(png[0], 0x89);
  assert.equal(png[1], 0x50);
  assert.equal(png[2], 0x4e);
  assert.equal(png[3], 0x47);
  // Width and height in IHDR chunk (bytes 16..23)
  assert.equal(png.readUInt32BE(16), 256);
  assert.equal(png.readUInt32BE(20), 256);
});

test('generateDrawerIcoBuffer produces a valid Windows ICO file wrapping a 256x256 PNG', () => {
  const ico = generateDrawerIcoBuffer();
  assert.ok(Buffer.isBuffer(ico));
  // ICO header: reserved=0, type=1 (ICO), count=1
  assert.equal(ico.readUInt16LE(0), 0);
  assert.equal(ico.readUInt16LE(2), 1);
  assert.equal(ico.readUInt16LE(4), 1);
  // Width=0, Height=0 in ICO directory entry means 256x256
  assert.equal(ico[6], 0);
  assert.equal(ico[7], 0);
});

test('ensureAppIcons writes icon.png and icon.ico to target directory', () => {
  const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'drawer-icons-'));
  const result = ensureAppIcons(tmpDir);
  assert.equal(fs.existsSync(result.pngPath), true);
  assert.equal(fs.existsSync(result.icoPath), true);
  assert.ok(fs.statSync(result.icoPath).size > 500);
  fs.rmSync(tmpDir, { recursive: true, force: true });
});
