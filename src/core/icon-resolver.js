const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');

function expandWindowsEnvVars(str = '') {
  if (typeof str !== 'string') return '';
  return str.replace(/%([^%]+)%/g, (full, name) => {
    return process.env[name] || full;
  });
}

function parseUrlShortcutFile(rawText = '') {
  if (typeof rawText !== 'string') {
    return { iconFile: '', iconIndex: 0, url: '' };
  }
  let iconFile = '';
  let iconIndex = 0;
  let url = '';

  const lines = rawText.split(/\r?\n/);
  for (const rawLine of lines) {
    const line = rawLine.trim();
    if (!line || line.startsWith(';') || line.startsWith('[')) continue;
    const eqIdx = line.indexOf('=');
    if (eqIdx === -1) continue;
    const key = line.slice(0, eqIdx).trim().toLowerCase();
    const val = line
      .slice(eqIdx + 1)
      .trim()
      .replace(/^"(.*)"$/, '$1');

    if (key === 'iconfile') {
      iconFile = expandWindowsEnvVars(val);
    } else if (key === 'iconindex') {
      const parsed = parseInt(val, 10);
      iconIndex = Number.isNaN(parsed) ? 0 : parsed;
    } else if (key === 'url') {
      url = val;
    }
  }

  return { iconFile, iconIndex, url };
}

function resolveIconSourcePath(filePath, shortcutReader = null) {
  if (!filePath || typeof filePath !== 'string') {
    return { iconPath: '', kind: 'unknown' };
  }

  const ext = path.extname(filePath).toLowerCase();

  if (ext === '.url' && fs.existsSync(filePath)) {
    try {
      const content = fs.readFileSync(filePath, 'utf8');
      const parsed = parseUrlShortcutFile(content);
      if (parsed.iconFile && fs.existsSync(parsed.iconFile)) {
        const iconExt = path.extname(parsed.iconFile).toLowerCase();
        return {
          iconPath: parsed.iconFile,
          kind:
            iconExt === '.ico'
              ? 'ico'
              : iconExt === '.png'
              ? 'png'
              : 'executable',
        };
      }
    } catch {
      // Fallback to original filePath
    }
  }

  if (ext === '.lnk' && typeof shortcutReader === 'function') {
    try {
      const details = shortcutReader(filePath);
      if (details) {
        const candidateIcon = expandWindowsEnvVars(details.icon || '');
        if (candidateIcon && fs.existsSync(candidateIcon)) {
          const iconExt = path.extname(candidateIcon).toLowerCase();
          return {
            iconPath: candidateIcon,
            kind:
              iconExt === '.ico'
                ? 'ico'
                : iconExt === '.png'
                ? 'png'
                : 'executable',
          };
        }
        const candidateTarget = expandWindowsEnvVars(details.target || '');
        if (candidateTarget && fs.existsSync(candidateTarget)) {
          return {
            iconPath: candidateTarget,
            kind: 'executable',
          };
        }
      }
    } catch {
      // Fallback to original .lnk
    }
  }

  return {
    iconPath: filePath,
    kind: ext === '.ico' ? 'ico' : ext === '.png' ? 'png' : 'shell',
  };
}

function getWin32HelperPath() {
  const baseCandidate = path.join(
    __dirname,
    '..',
    '..',
    'assets',
    'win32-helper.exe'
  );
  const unpackedCandidate = baseCandidate.replace(
    'app.asar',
    'app.asar.unpacked'
  );
  if (fs.existsSync(unpackedCandidate)) {
    return unpackedCandidate;
  }
  return baseCandidate;
}

function extractWindowsIconDataUrl(filePath, shortcutReader = null) {
  if (!filePath || typeof filePath !== 'string') return '';
  const resolved = resolveIconSourcePath(filePath, shortcutReader);
  const targetToExtract = resolved.iconPath || filePath;

  if (targetToExtract.toLowerCase().endsWith('.png') && fs.existsSync(targetToExtract)) {
    try {
      const buf = fs.readFileSync(targetToExtract);
      return `data:image/png;base64,${buf.toString('base64')}`;
    } catch {
      // continue
    }
  }

  const helperExe = getWin32HelperPath();
  if (process.platform === 'win32' && fs.existsSync(helperExe)) {
    try {
      const out = execFileSync(helperExe, ['extract-icon', targetToExtract], {
        encoding: 'utf8',
        windowsHide: true,
        timeout: 4000,
        maxBuffer: 10 * 1024 * 1024,
      }).trim();
      if (out.startsWith('data:image/png;base64,')) {
        return out;
      }
    } catch {
      // Fallback if helper fails on resolved path: try original filePath
    }

    if (targetToExtract !== filePath && fs.existsSync(filePath)) {
      try {
        const out2 = execFileSync(helperExe, ['extract-icon', filePath], {
          encoding: 'utf8',
          windowsHide: true,
          timeout: 4000,
          maxBuffer: 10 * 1024 * 1024,
        }).trim();
        if (out2.startsWith('data:image/png;base64,')) {
          return out2;
        }
      } catch {
        // Ignore
      }
    }
  }

  return '';
}

module.exports = {
  expandWindowsEnvVars,
  parseUrlShortcutFile,
  resolveIconSourcePath,
  getWin32HelperPath,
  extractWindowsIconDataUrl,
};
