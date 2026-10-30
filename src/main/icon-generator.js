const fs = require('node:fs');
const path = require('node:path');
const zlib = require('node:zlib');

// Precomputed CRC32 table for PNG chunks
const CRC_TABLE = new Uint32Array(256);
for (let n = 0; n < 256; n++) {
  let c = n;
  for (let k = 0; k < 8; k++) {
    c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  }
  CRC_TABLE[n] = c >>> 0;
}

function crc32(buf) {
  let c = 0xffffffff;
  for (let i = 0; i < buf.length; i++) {
    c = CRC_TABLE[(c ^ buf[i]) & 0xff] ^ (c >>> 8);
  }
  return (c ^ 0xffffffff) >>> 0;
}

function makePngChunk(type, data) {
  const typeBuf = Buffer.from(type, 'ascii');
  const lenBuf = Buffer.alloc(4);
  lenBuf.writeUInt32BE(data.length, 0);
  const crcInput = Buffer.concat([typeBuf, data]);
  const crcBuf = Buffer.alloc(4);
  crcBuf.writeUInt32BE(crc32(crcInput), 0);
  return Buffer.concat([lenBuf, typeBuf, data, crcBuf]);
}

function roundedRectDistance(px, py, x0, y0, x1, y1, r) {
  const cx = Math.max(x0 + r, Math.min(px, x1 - r));
  const cy = Math.max(y0 + r, Math.min(py, y1 - r));
  return Math.hypot(px - cx, py - cy) - r;
}

/**
 * Generates a 32-bit RGBA PNG buffer depicting a modern blue rounded-square Drawer
 * with 4 Discord-style rounded mini-squares inside.
 */
function generateDrawerPngBuffer(size = 256) {
  const rawData = Buffer.alloc(size * (size * 4 + 1));
  const margin = Math.round(size * 0.08);
  const radius = Math.round(size * 0.22);

  // 2x2 inner mini-icons geometry
  const innerPad = Math.round(size * 0.24);
  const cellGap = Math.round(size * 0.06);
  const cellSize = Math.round((size - innerPad * 2 - cellGap) / 2);
  const cellRadius = Math.round(cellSize * 0.28);

  const cells = [
    { x0: innerPad, y0: innerPad, color: [147, 197, 253] }, // top-left (soft sky blue)
    { x0: innerPad + cellSize + cellGap, y0: innerPad, color: [191, 219, 254] }, // top-right
    { x0: innerPad, y0: innerPad + cellSize + cellGap, color: [191, 219, 254] }, // bottom-left
    {
      x0: innerPad + cellSize + cellGap,
      y0: innerPad + cellSize + cellGap,
      color: [96, 165, 250],
    }, // bottom-right
  ];

  for (let y = 0; y < size; y++) {
    const rowStart = y * (size * 4 + 1);
    rawData[rowStart] = 0; // Filter type 0 (None)

    const t = y / Math.max(1, size - 1);
    // Rich blue vertical gradient (#2563EB -> #1D4ED8)
    const bgR = Math.round(37 * (1 - t) + 29 * t);
    const bgG = Math.round(99 * (1 - t) + 78 * t);
    const bgB = Math.round(235 * (1 - t) + 216 * t);

    for (let x = 0; x < size; x++) {
      const pxOffset = rowStart + 1 + x * 4;
      const dOuter = roundedRectDistance(
        x,
        y,
        margin,
        margin,
        size - margin,
        size - margin,
        radius
      );

      if (dOuter > 1.0) {
        rawData[pxOffset] = 0;
        rawData[pxOffset + 1] = 0;
        rawData[pxOffset + 2] = 0;
        rawData[pxOffset + 3] = 0;
        continue;
      }

      // Smooth anti-aliased edge for outer blue rounded square
      const outerAlpha = dOuter < 0 ? 1 : Math.max(0, 1 - dOuter);
      let r = bgR;
      let g = bgG;
      let b = bgB;

      // Subtle glossy border around outer square
      if (dOuter > -3.5) {
        r = Math.min(255, r + 35);
        g = Math.min(255, g + 50);
        b = Math.min(255, b + 20);
      }

      // Check 2x2 inner mini-icons
      for (const cell of cells) {
        const dCell = roundedRectDistance(
          x,
          y,
          cell.x0,
          cell.y0,
          cell.x0 + cellSize,
          cell.y0 + cellSize,
          cellRadius
        );
        if (dCell <= 0.8) {
          const blend = dCell < 0 ? 0.95 : Math.max(0, 0.95 * (1 - dCell));
          r = Math.round(r * (1 - blend) + cell.color[0] * blend);
          g = Math.round(g * (1 - blend) + cell.color[1] * blend);
          b = Math.round(b * (1 - blend) + cell.color[2] * blend);
        }
      }

      rawData[pxOffset] = r;
      rawData[pxOffset + 1] = g;
      rawData[pxOffset + 2] = b;
      rawData[pxOffset + 3] = Math.round(outerAlpha * 255);
    }
  }

  const signature = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(size, 0);
  ihdr.writeUInt32BE(size, 4);
  ihdr[8] = 8; // bit depth
  ihdr[9] = 6; // color type RGBA
  ihdr[10] = 0;
  ihdr[11] = 0;
  ihdr[12] = 0;

  const idatData = zlib.deflateSync(rawData);
  return Buffer.concat([
    signature,
    makePngChunk('IHDR', ihdr),
    makePngChunk('IDAT', idatData),
    makePngChunk('IEND', Buffer.alloc(0)),
  ]);
}

/**
 * Generates a standard Windows .ico file containing a 256x256 PNG icon.
 */
function generateDrawerIcoBuffer() {
  const pngBuf = generateDrawerPngBuffer(256);
  const header = Buffer.alloc(6);
  header.writeUInt16LE(0, 0); // Reserved
  header.writeUInt16LE(1, 2); // Type: 1 = ICO
  header.writeUInt16LE(1, 4); // Count: 1 image

  const dirEntry = Buffer.alloc(16);
  dirEntry[0] = 0; // Width 256 (0 means 256)
  dirEntry[1] = 0; // Height 256 (0 means 256)
  dirEntry[2] = 0; // Color palette
  dirEntry[3] = 0; // Reserved
  dirEntry.writeUInt16LE(1, 4); // Color planes
  dirEntry.writeUInt16LE(32, 6); // Bits per pixel
  dirEntry.writeUInt32LE(pngBuf.length, 8); // Image byte size
  dirEntry.writeUInt32LE(22, 12); // Offset (6 + 16 = 22)

  return Buffer.concat([header, dirEntry, pngBuf]);
}

function ensureAppIcons(assetsDir = path.join(__dirname, '..', '..', 'assets')) {
  fs.mkdirSync(assetsDir, { recursive: true });
  const pngPath = path.join(assetsDir, 'icon.png');
  const icoPath = path.join(assetsDir, 'icon.ico');
  fs.writeFileSync(pngPath, generateDrawerPngBuffer(256));
  fs.writeFileSync(icoPath, generateDrawerIcoBuffer());
  return { pngPath, icoPath };
}

if (require.main === module) {
  const paths = ensureAppIcons();
  console.log('Generated Drawer icons:', paths);
}

module.exports = {
  generateDrawerPngBuffer,
  generateDrawerIcoBuffer,
  ensureAppIcons,
};
