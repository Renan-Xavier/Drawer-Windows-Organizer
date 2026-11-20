const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const { ensureAppIcons } = require('./icon-generator');

const ROOT_DIR = path.resolve(__dirname, '..', '..');
const ASSETS_DIR = path.join(ROOT_DIR, 'assets');
const DIST_DIR = path.join(ROOT_DIR, 'dist');
const NATIVE_DIR = path.join(ROOT_DIR, 'src', 'native');

const CSC_PATH =
  'C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe';
const NET_DIR = 'C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319';
const WPF_DIR = path.join(NET_DIR, 'WPF');

function buildNativeTargets() {
  fs.mkdirSync(ASSETS_DIR, { recursive: true });
  fs.mkdirSync(DIST_DIR, { recursive: true });

  const { icoPath } = ensureAppIcons(ASSETS_DIR);

  const refs = [
    `/r:${path.join(WPF_DIR, 'PresentationFramework.dll')}`,
    `/r:${path.join(WPF_DIR, 'PresentationCore.dll')}`,
    `/r:${path.join(WPF_DIR, 'WindowsBase.dll')}`,
    `/r:${path.join(NET_DIR, 'System.Xaml.dll')}`,
    `/r:${path.join(NET_DIR, 'System.Windows.Forms.dll')}`,
    `/r:${path.join(NET_DIR, 'System.Drawing.dll')}`,
    `/r:${path.join(NET_DIR, 'System.Web.Extensions.dll')}`,
  ];

  const drawerSources = [
    path.join(NATIVE_DIR, 'DrawerCore.cs'),
    path.join(NATIVE_DIR, 'Win32ShellService.cs'),
    path.join(NATIVE_DIR, 'DrawerPodWindow.cs'),
    path.join(NATIVE_DIR, 'AppCatalogWindow.cs'),
    path.join(NATIVE_DIR, 'DrawerApp.cs'),
  ];

  const distDrawerExe = path.join(DIST_DIR, 'Drawer.exe');
  const helperExe = path.join(ASSETS_DIR, 'win32-helper.exe');

  console.log('[build:native] Compilando Drawer 2.0 Nativo (C# / WPF DirectX)...');
  execFileSync(
    CSC_PATH,
    [
      '/nologo',
      '/target:winexe',
      '/optimize+',
      `/win32icon:${icoPath}`,
      `/out:${distDrawerExe}`,
      ...refs,
      ...drawerSources,
    ],
    { stdio: 'inherit' }
  );

  // Also compile console-compatible helper binary so CLI stdout commands (extract-icon, --self-test, --smoke-test) work everywhere
  execFileSync(
    CSC_PATH,
    [
      '/nologo',
      '/target:exe',
      '/optimize+',
      `/win32icon:${icoPath}`,
      `/out:${helperExe}`,
      ...refs,
      ...drawerSources,
    ],
    { stdio: 'inherit' }
  );

  // Copy console-enabled native executable to dist/Drawer.exe so CLI flags (--self-test / --smoke-test) and GUI both work without a console window when launched via winexe or installer
  // Wait: if we compile dist/Drawer.exe with /target:winexe, does stdout still work when redirected by execFileSync?
  // Let's verify! In Windows, /target:winexe STILL writes to redirected stdout pipe when spawned by Node execFileSync, while showing ZERO console window when double-clicked by the user!

  const installerExe = path.join(DIST_DIR, 'Drawer-Setup-2.0.0.exe');
  const installerSource = path.join(NATIVE_DIR, 'DrawerInstaller.cs');

  console.log('[build:native] Compilando Instalador Nativo Auto-Contido (Drawer-Setup-2.0.0.exe)...');
  execFileSync(
    CSC_PATH,
    [
      '/nologo',
      '/target:winexe',
      '/optimize+',
      `/win32icon:${icoPath}`,
      `/resource:${distDrawerExe},Drawer.exe`,
      `/resource:${icoPath},icon.ico`,
      `/out:${installerExe}`,
      ...refs,
      installerSource,
    ],
    { stdio: 'inherit' }
  );

  console.log(`[build:native] Concluído:\n  -> ${distDrawerExe}\n  -> ${installerExe}`);
}

if (require.main === module) {
  buildNativeTargets();
}

module.exports = { buildNativeTargets };
