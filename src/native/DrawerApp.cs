using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace DrawerNative
{
    public interface IDrawerAppController
    {
        bool IsPeekModeActive { get; }
        bool IsShowDesktopRaised { get; }
        bool IsAnyContextMenuOpen { get; }
        string ActiveDrawerId { get; }
        void ActivatePod(string drawerId, bool closeOtherHoverPods);
        void SyncAllPodsLayering();
        void RequestSyncAllPodsLayering();
        DrawerAppState GetState();
        string GetEffectiveScale();
        string GetBackgroundMode();
        DrawerModel CreateNewDrawer();
        void RenameDrawer(string drawerId, string newName);
        void MoveDrawer(string drawerId, int x, int y);
        void SetDrawerDirection(string drawerId, string direction);
        void SetDrawerDockedEdge(string drawerId, string edge);
        void DeleteDrawer(string drawerId);
        void AddFilesToDrawer(string drawerId, IEnumerable<string> filePaths);
        void PickFilesForDrawer(string drawerId);
        void RestoreItemToDesktop(string drawerId, string itemId);
        void RestoreItemToDesktopAtPosition(string drawerId, string itemId, int screenX, int screenY);
        void MoveItemBetweenDrawers(string fromDrawerId, string itemId, string toDrawerId);
        void PromptRenameItem(string drawerId, DrawerItem item);
        void LaunchItem(string drawerId, DrawerItem item);
        void UpdateSettings(string iconScaleMode, string backgroundMode, bool? launchAtStartup, bool? moveDesktopFilesToVault);
        void OpenCatalogForDrawer(string drawerId);
        void OpenSettingsWindow();
        bool IsScreenPointInsideAnyPod(int screenX, int screenY);
        string FindDrawerPodAtScreenPoint(int screenX, int screenY);
    }

    public class DrawerAppController : IDrawerAppController
    {
        private readonly DrawerStateStore _store;
        private readonly Dictionary<string, DrawerPodWindow> _podWindows;
        private readonly DispatcherTimer _layerTimer;
        private readonly uint _currentPid;
        private AppCatalogWindow _catalogWindow;
        private Forms.NotifyIcon _trayIcon;
        private Window _hotkeySinkWindow;
        private IntPtr _bottomProbeHwnd;
        private bool _isPeekModeActive;
        private bool _isShowDesktopRaised;
        private string _activeDrawerId;
        private bool _syncLayeringQueued;
        private int _layerTickCounter;

        private const int HOTKEY_ID_PEEK = 0x4452; // Ctrl + Shift + D

        public bool IsPeekModeActive
        {
            get { return _isPeekModeActive; }
        }

        public bool IsShowDesktopRaised
        {
            get { return _isShowDesktopRaised; }
        }

        public string ActiveDrawerId
        {
            get { return _activeDrawerId; }
        }

        public bool IsAnyContextMenuOpen
        {
            get
            {
                foreach (DrawerPodWindow pod in _podWindows.Values)
                {
                    if (pod.IsContextMenuOpen) return true;
                }
                return false;
            }
        }

        public DrawerAppController(DrawerStateStore store)
        {
            _store = store;
            _podWindows = new Dictionary<string, DrawerPodWindow>();
            _currentPid = (uint)Process.GetCurrentProcess().Id;

            _layerTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(140)
            };
            _layerTimer.Tick += OnLayerTimerTick;
        }

        private void OnLayerTimerTick(object sender, EventArgs e)
        {
            if (IsAnyContextMenuOpen) return;

            bool showDesktopNow = Win32ShellService.IsShowDesktopStateActive(_bottomProbeHwnd, _currentPid);
            _layerTickCounter++;

            if (showDesktopNow != _isShowDesktopRaised)
            {
                _isShowDesktopRaised = showDesktopNow;
                SyncAllPodsLayering();
            }
            else if (_layerTickCounter % 4 == 0)
            {
                SyncAllPodsLayering();
            }
        }

        public void ActivatePod(string drawerId, bool closeOtherHoverPods)
        {
            if (!string.IsNullOrEmpty(drawerId))
            {
                _activeDrawerId = drawerId;
            }

            if (closeOtherHoverPods)
            {
                foreach (DrawerPodWindow pod in _podWindows.Values)
                {
                    if (pod.DrawerId != drawerId)
                    {
                        pod.CloseUnpinnedHover();
                    }
                }
            }

            SyncAllPodsLayering();
        }

        public void RequestSyncAllPodsLayering()
        {
            if (_syncLayeringQueued || IsAnyContextMenuOpen) return;
            _syncLayeringQueued = true;

            if (Application.Current != null && Application.Current.Dispatcher != null)
            {
                Application.Current.Dispatcher.BeginInvoke(
                    new Action(SyncAllPodsLayering),
                    DispatcherPriority.Render);
            }
            else
            {
                SyncAllPodsLayering();
            }
        }

        public void SyncAllPodsLayering()
        {
            _syncLayeringQueued = false;
            if (IsAnyContextMenuOpen) return;

            var topmostPods = new List<DrawerPodWindow>();
            var bottomPods = new List<DrawerPodWindow>();

            foreach (DrawerPodWindow pod in _podWindows.Values)
            {
                if (pod.ShouldBeTopmost)
                    topmostPods.Add(pod);
                else
                    bottomPods.Add(pod);
            }

            // 1. For HWND_TOPMOST pods: SetWindowPos(HWND_TOPMOST) puts the window at the FRONT of the topmost stack.
            // Apply from BACK to FRONT: closed pods -> other open pods -> _activeDrawerId LAST (front-most!).
            foreach (DrawerPodWindow pod in topmostPods)
            {
                if (pod.DrawerId != _activeDrawerId && !pod.IsDrawerOpen)
                    pod.RefreshLayering();
            }
            foreach (DrawerPodWindow pod in topmostPods)
            {
                if (pod.DrawerId != _activeDrawerId && pod.IsDrawerOpen)
                    pod.RefreshLayering();
            }
            foreach (DrawerPodWindow pod in topmostPods)
            {
                if (pod.DrawerId == _activeDrawerId)
                    pod.RefreshLayering();
            }

            // 2. For HWND_BOTTOM pods: SetWindowPos(HWND_BOTTOM) puts the window at the BACK of the bottom stack.
            // Apply from FRONT to BACK: _activeDrawerId FIRST (front-most!) -> other open pods -> closed pods LAST (back-most!).
            foreach (DrawerPodWindow pod in bottomPods)
            {
                if (pod.DrawerId == _activeDrawerId)
                    pod.RefreshLayering();
            }
            foreach (DrawerPodWindow pod in bottomPods)
            {
                if (pod.DrawerId != _activeDrawerId && pod.IsDrawerOpen)
                    pod.RefreshLayering();
            }
            foreach (DrawerPodWindow pod in bottomPods)
            {
                if (pod.DrawerId != _activeDrawerId && !pod.IsDrawerOpen)
                    pod.RefreshLayering();
            }

            if (_bottomProbeHwnd != IntPtr.Zero && Win32ShellService.IsWindow(_bottomProbeHwnd))
            {
                Win32ShellService.SetWindowPos(
                    _bottomProbeHwnd,
                    Win32ShellService.HWND_BOTTOM,
                    0, 0, 0, 0,
                    Win32ShellService.SWP_NOMOVE | Win32ShellService.SWP_NOSIZE | Win32ShellService.SWP_NOACTIVATE | Win32ShellService.SWP_NOSENDCHANGING);
            }
        }

        public void Initialize(bool isSmokeTest)
        {
            DrawerAppState st = _store.Load();

            // Refresh icons for existing items if missing, and auto-trim transparent margins on existing cached icons
            bool iconsUpdated = false;
            foreach (DrawerModel d in st.drawers)
            {
                foreach (DrawerItem it in d.items)
                {
                    if (string.IsNullOrEmpty(it.iconDataUrl))
                    {
                        string pathToUse = !string.IsNullOrEmpty(it.targetPath) && (File.Exists(it.targetPath) || Directory.Exists(it.targetPath))
                            ? it.targetPath
                            : it.originalDesktopPath;
                        if (!string.IsNullOrEmpty(pathToUse) && (File.Exists(pathToUse) || Directory.Exists(pathToUse)))
                        {
                            string fresh = Win32ShellService.ExtractHighResIconDataUrl(pathToUse);
                            if (!string.IsNullOrEmpty(fresh))
                            {
                                it.iconDataUrl = fresh;
                                iconsUpdated = true;
                            }
                        }
                    }
                    else
                    {
                        string trimmed = Win32ShellService.TrimDataUrlTransparentMargins(it.iconDataUrl);
                        if (!string.IsNullOrEmpty(trimmed) && trimmed != it.iconDataUrl)
                        {
                            it.iconDataUrl = trimmed;
                            iconsUpdated = true;
                        }
                    }
                }
            }
            if (iconsUpdated) _store.Save();

            if (!isSmokeTest)
            {
                string exePath = EnsurePersistentLocalInstallationPath();
                StartupRegistryManager.SyncStartup(st.settings.launchAtStartup, exePath);
                InitializeTrayIcon();
                InitializeGlobalHotkeyWindow();
            }

            SyncPodWindows();
            if (!isSmokeTest)
            {
                _layerTimer.Start();
            }
        }

        private static string EnsurePersistentLocalInstallationPath()
        {
            string currentExe = Process.GetCurrentProcess().MainModule.FileName;
            try
            {
                string standardDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs",
                    "Drawer");
                string standardExe = Path.Combine(standardDir, "Drawer.exe");

                if (!string.Equals(
                    Path.GetFullPath(currentExe),
                    Path.GetFullPath(standardExe),
                    StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(standardDir);
                    File.Copy(currentExe, standardExe, true);
                }

                if (File.Exists(standardExe))
                {
                    return standardExe;
                }
            }
            catch { }
            return currentExe;
        }

        public int ActivePodCount
        {
            get { return _podWindows.Count; }
        }

        public IEnumerable<DrawerPodWindow> GetPodWindows()
        {
            return _podWindows.Values;
        }

        private void InitializeGlobalHotkeyWindow()
        {
            _hotkeySinkWindow = new Window
            {
                Width = 1,
                Height = 1,
                Left = -32000,
                Top = -32000,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                IsHitTestVisible = false
            };
            _hotkeySinkWindow.SourceInitialized += delegate
            {
                IntPtr hwnd = new WindowInteropHelper(_hotkeySinkWindow).Handle;
                _bottomProbeHwnd = hwnd;
                Win32ShellService.ConfigurePodToolWindowStyle(hwnd);
                Win32ShellService.SetWindowPos(
                    hwnd,
                    Win32ShellService.HWND_BOTTOM,
                    0, 0, 0, 0,
                    Win32ShellService.SWP_NOMOVE | Win32ShellService.SWP_NOSIZE | Win32ShellService.SWP_NOACTIVATE | Win32ShellService.SWP_NOSENDCHANGING);
                Win32ShellService.RegisterHotKey(
                    hwnd,
                    HOTKEY_ID_PEEK,
                    Win32ShellService.MOD_CONTROL | Win32ShellService.MOD_SHIFT,
                    Win32ShellService.VK_D);

                HwndSource src = HwndSource.FromHwnd(hwnd);
                if (src != null)
                {
                    src.AddHook(HotkeyWndProc);
                }
            };
            _hotkeySinkWindow.Show();
        }

        private IntPtr HotkeyWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32ShellService.WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID_PEEK)
            {
                handled = true;
                TogglePeekMode();
            }
            return IntPtr.Zero;
        }

        public void TogglePeekMode()
        {
            _isPeekModeActive = !_isPeekModeActive;
            SyncAllPodsLayering();
            RebuildTrayMenu();
        }

        private static System.Drawing.Icon LoadOrGenerateTrayIcon()
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                System.Drawing.Icon assoc = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (assoc != null) return assoc;
            }
            catch { }

            var bmp = new System.Drawing.Bitmap(32, 32);
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.FromArgb(37, 99, 235));
                using (var br = new System.Drawing.SolidBrush(System.Drawing.Color.White))
                {
                    g.FillRectangle(br, 6, 6, 8, 8);
                    g.FillRectangle(br, 18, 6, 8, 8);
                    g.FillRectangle(br, 6, 18, 8, 8);
                    g.FillRectangle(br, 18, 18, 8, 8);
                }
            }
            return System.Drawing.Icon.FromHandle(bmp.GetHicon());
        }

        private void InitializeTrayIcon()
        {
            _trayIcon = new Forms.NotifyIcon
            {
                Icon = LoadOrGenerateTrayIcon(),
                Text = "Drawer 2.0 — Pods Nativos & Bordas Magnéticas",
                Visible = true
            };
            _trayIcon.DoubleClick += delegate { OpenSettingsWindow(); };
            RebuildTrayMenu();
        }

        private void RebuildTrayMenu()
        {
            if (_trayIcon == null) return;
            DrawerAppState st = _store.GetState();

            var menu = new Forms.ContextMenuStrip();

            var headerItem = new Forms.ToolStripMenuItem("Drawer 2.0 — Pods Nativos (C#/WPF)")
            {
                Enabled = false
            };
            menu.Items.Add(headerItem);
            menu.Items.Add(new Forms.ToolStripSeparator());

            var catItem = new Forms.ToolStripMenuItem("Catálogo Rápido de Apps & Configurações...");
            catItem.Click += delegate { OpenSettingsWindow(); };
            menu.Items.Add(catItem);

            var newItem = new Forms.ToolStripMenuItem("+ Nova Gaveta");
            newItem.Click += delegate { CreateNewDrawer(); };
            menu.Items.Add(newItem);

            var peekItem = new Forms.ToolStripMenuItem("Modo Chamada Rápida (Ctrl+Shift+D)")
            {
                Checked = _isPeekModeActive
            };
            peekItem.Click += delegate { TogglePeekMode(); };
            menu.Items.Add(peekItem);

            menu.Items.Add(new Forms.ToolStripSeparator());

            var startupItem = new Forms.ToolStripMenuItem("Inicializar com o Windows")
            {
                Checked = st.settings.launchAtStartup
            };
            startupItem.Click += delegate
            {
                UpdateSettings(null, null, !st.settings.launchAtStartup, null);
            };
            menu.Items.Add(startupItem);

            var vaultItem = new Forms.ToolStripMenuItem("Guardar arquivos da Área de Trabalho no Cofre")
            {
                Checked = st.settings.moveDesktopFilesToVault
            };
            vaultItem.Click += delegate
            {
                UpdateSettings(null, null, null, !st.settings.moveDesktopFilesToVault);
            };
            menu.Items.Add(vaultItem);

            menu.Items.Add(new Forms.ToolStripSeparator());

            var exitItem = new Forms.ToolStripMenuItem("Sair do Drawer");
            exitItem.Click += delegate { Shutdown(); };
            menu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = menu;
        }

        public void SyncPodWindows()
        {
            DrawerAppState st = _store.GetState();
            var activeIds = new HashSet<string>();

            foreach (DrawerModel d in st.drawers)
            {
                activeIds.Add(d.id);
                DrawerPodWindow existing;
                if (_podWindows.TryGetValue(d.id, out existing))
                {
                    existing.UpdateDrawerModel(d);
                }
                else
                {
                    var pod = new DrawerPodWindow(this, d);
                    _podWindows[d.id] = pod;
                    if (string.IsNullOrEmpty(_activeDrawerId))
                    {
                        _activeDrawerId = d.id;
                    }
                    pod.RenderPod();
                    pod.Show();
                }
            }

            var toRemove = new List<string>();
            foreach (string id in _podWindows.Keys)
            {
                if (!activeIds.Contains(id))
                {
                    toRemove.Add(id);
                }
            }

            foreach (string id in toRemove)
            {
                try { _podWindows[id].Close(); } catch { }
                _podWindows.Remove(id);
            }

            SyncAllPodsLayering();

            if (_catalogWindow != null && _catalogWindow.IsLoaded)
            {
                _catalogWindow.RefreshUI();
            }
            RebuildTrayMenu();
        }

        public DrawerAppState GetState()
        {
            return _store.GetState();
        }

        public string GetEffectiveScale()
        {
            return _store.GetEffectiveScale();
        }

        public string GetBackgroundMode()
        {
            return _store.GetState().settings.backgroundMode ?? "blue";
        }

        public DrawerModel CreateNewDrawer()
        {
            DrawerAppState st = _store.GetState();
            int count = st.drawers.Count;
            int yPos = Math.Min((int)SystemParameters.WorkArea.Height - 140, 60 + count * 115);
            DrawerModel created = _store.CreateDrawer(
                string.Format("Gaveta {0}", count + 1),
                36,
                yPos,
                "none");
            SyncPodWindows();
            return created;
        }

        public void RenameDrawer(string drawerId, string newName)
        {
            _store.RenameDrawer(drawerId, newName);
            SyncPodWindows();
        }

        public void MoveDrawer(string drawerId, int x, int y)
        {
            ScaleMetrics m = DrawerGeometryEngine.GetScaleMetrics(GetEffectiveScale());
            int screenW = (int)SystemParameters.WorkArea.Width;
            int screenH = (int)SystemParameters.WorkArea.Height;
            _store.MoveDrawer(drawerId, x, y, m.DrawerSize, screenW, screenH);
            SyncPodWindows();
        }

        public void SetDrawerDirection(string drawerId, string direction)
        {
            _store.SetDrawerDirection(drawerId, direction);
            SyncPodWindows();
        }

        public void SetDrawerDockedEdge(string drawerId, string edge)
        {
            ScaleMetrics m = DrawerGeometryEngine.GetScaleMetrics(GetEffectiveScale());
            int screenW = (int)SystemParameters.WorkArea.Width;
            int screenH = (int)SystemParameters.WorkArea.Height;
            _store.SetDrawerDockedEdge(drawerId, edge, m.DrawerSize, screenW, screenH);
            SyncPodWindows();
        }

        public void DeleteDrawer(string drawerId)
        {
            _store.DeleteDrawer(drawerId);
            SyncPodWindows();
        }

        public void AddFilesToDrawer(string drawerId, IEnumerable<string> filePaths)
        {
            if (filePaths == null) return;
            foreach (string fp in filePaths)
            {
                if (string.IsNullOrWhiteSpace(fp)) continue;
                _store.AddItemToDrawer(drawerId, fp, null);
            }
            SyncPodWindows();
        }

        public void PickFilesForDrawer(string drawerId)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Adicionar Programas, Jogos ou Atalhos à Gaveta",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Multiselect = true,
                DereferenceLinks = false, // CRITICAL: Preserves .lnk and .url files with their arguments & working directory!
                Filter = "Todos os Programas e Atalhos (*.lnk;*.url;*.exe;*.*)|*.lnk;*.url;*.exe;*.*"
            };

            if (dlg.ShowDialog() == true && dlg.FileNames != null && dlg.FileNames.Length > 0)
            {
                AddFilesToDrawer(drawerId, dlg.FileNames);
            }
        }

        public void RestoreItemToDesktop(string drawerId, string itemId)
        {
            _store.MoveItemToDesktop(drawerId, itemId);
            SyncPodWindows();
        }

        public void RestoreItemToDesktopAtPosition(string drawerId, string itemId, int screenX, int screenY)
        {
            string restoredPath = _store.MoveItemToDesktop(drawerId, itemId);
            SyncPodWindows();
            if (!string.IsNullOrEmpty(restoredPath))
            {
                Win32ShellService.PositionDesktopIconAtScreenPoint(restoredPath, screenX, screenY);
            }
        }

        public void MoveItemBetweenDrawers(string fromDrawerId, string itemId, string toDrawerId)
        {
            _store.MoveItemBetweenDrawers(fromDrawerId, itemId, toDrawerId);
            SyncPodWindows();
        }

        public void PromptRenameItem(string drawerId, DrawerItem item)
        {
            if (item == null) return;

            var win = new Window
            {
                Title = "Renomear Item na Gaveta",
                Width = 360,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                Foreground = Brushes.White,
                Topmost = true
            };

            var stack = new StackPanel { Margin = new Thickness(16) };
            stack.Children.Add(new TextBlock
            {
                Text = "Novo nome do atalho:",
                Margin = new Thickness(0, 0, 0, 8),
                FontWeight = FontWeights.SemiBold
            });

            var box = new TextBox
            {
                Text = item.name,
                Height = 28,
                Padding = new Thickness(6, 3, 6, 3),
                Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                Foreground = Brushes.White
            };
            stack.Children.Add(box);

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };

            var saveBtn = new Button
            {
                Content = "Salvar",
                Width = 80,
                Height = 28,
                Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                Foreground = Brushes.White,
                IsDefault = true
            };
            saveBtn.Click += delegate
            {
                if (!string.IsNullOrWhiteSpace(box.Text))
                {
                    _store.RenameItem(drawerId, item.id, box.Text.Trim());
                    SyncPodWindows();
                }
                win.Close();
            };

            btnRow.Children.Add(saveBtn);
            stack.Children.Add(btnRow);
            win.Content = stack;
            win.Loaded += delegate
            {
                box.Focus();
                box.SelectAll();
            };
            win.ShowDialog();
        }

        public void LaunchItem(string drawerId, DrawerItem item)
        {
            string err;
            bool ok = Win32ShellService.OpenItemFaithfully(item, out err);
            if (_isPeekModeActive && ok)
            {
                _isPeekModeActive = false;
                foreach (DrawerPodWindow pod in _podWindows.Values)
                {
                    pod.RefreshLayering();
                }
            }
            if (!ok && !string.IsNullOrEmpty(err))
            {
                MessageBox.Show(
                    string.Format("Não foi possível abrir \"{0}\":\n{1}", item != null ? item.name : "Atalho", err),
                    "Drawer 2.0",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        public void UpdateSettings(string iconScaleMode, string backgroundMode, bool? launchAtStartup, bool? moveDesktopFilesToVault)
        {
            DrawerAppState st = _store.GetState();
            if (!string.IsNullOrEmpty(iconScaleMode))
                st.settings.iconScaleMode = iconScaleMode;
            if (!string.IsNullOrEmpty(backgroundMode))
                st.settings.backgroundMode = (backgroundMode == "transparent") ? "transparent" : "blue";
            if (launchAtStartup.HasValue)
            {
                st.settings.launchAtStartup = launchAtStartup.Value;
                try
                {
                    string exePath = EnsurePersistentLocalInstallationPath();
                    StartupRegistryManager.SyncStartup(launchAtStartup.Value, exePath);
                }
                catch { }
            }
            if (moveDesktopFilesToVault.HasValue)
            {
                st.settings.moveDesktopFilesToVault = moveDesktopFilesToVault.Value;
                _store.SyncVaultWithSettings();
            }

            _store.Save();
            SyncPodWindows();
        }

        public void OpenCatalogForDrawer(string drawerId)
        {
            if (_catalogWindow != null && _catalogWindow.IsLoaded)
            {
                _catalogWindow.SelectDrawer(drawerId);
                _catalogWindow.Show();
                _catalogWindow.Activate();
                return;
            }

            _catalogWindow = new AppCatalogWindow(this, drawerId);
            _catalogWindow.Closed += delegate { _catalogWindow = null; };
            _catalogWindow.Show();
            _catalogWindow.Activate();
        }

        public void OpenSettingsWindow()
        {
            DrawerAppState st = _store.GetState();
            string firstId = (st.drawers != null && st.drawers.Count > 0) ? st.drawers[0].id : null;
            OpenCatalogForDrawer(firstId);
        }

        public bool IsScreenPointInsideAnyPod(int screenX, int screenY)
        {
            return !string.IsNullOrEmpty(FindDrawerPodAtScreenPoint(screenX, screenY));
        }

        public string FindDrawerPodAtScreenPoint(int screenX, int screenY)
        {
            foreach (DrawerPodWindow pod in _podWindows.Values)
            {
                if (screenX >= pod.Left && screenX <= pod.Left + pod.Width &&
                    screenY >= pod.Top && screenY <= pod.Top + pod.Height)
                {
                    return pod.DrawerId;
                }
            }
            return null;
        }

        public void Shutdown()
        {
            _layerTimer.Stop();
            if (_hotkeySinkWindow != null)
            {
                try
                {
                    IntPtr hwnd = new WindowInteropHelper(_hotkeySinkWindow).Handle;
                    if (hwnd != IntPtr.Zero)
                        Win32ShellService.UnregisterHotKey(hwnd, HOTKEY_ID_PEEK);
                    _hotkeySinkWindow.Close();
                }
                catch { }
            }
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            foreach (DrawerPodWindow pod in new List<DrawerPodWindow>(_podWindows.Values))
            {
                try { pod.Close(); } catch { }
            }
            _podWindows.Clear();
            if (Application.Current != null)
            {
                Application.Current.Shutdown();
            }
        }
    }

    public static class DrawerSelfTestRunner
    {
        public static int RunSelfTest()
        {
            var serializer = new JavaScriptSerializer();
            try
            {
                // 1. Geometry test
                GridDimensions g1 = DrawerGeometryEngine.ComputeDrawerGrid(1, "right", "medium");
                GridDimensions g3 = DrawerGeometryEngine.ComputeDrawerGrid(3, "right", "medium");
                GridDimensions g5 = DrawerGeometryEngine.ComputeDrawerGrid(5, "right", "medium");
                GridDimensions gDown2 = DrawerGeometryEngine.ComputeDrawerGrid(2, "down", "medium");
                bool geometryOk = (g1.Cols == 1 && g1.Rows == 1) &&
                                  (g3.Cols == 3 && g3.Rows == 1) &&
                                  (g5.Cols == 4 && g5.Rows == 2) &&
                                  (gDown2.Cols == 1 && gDown2.Rows == 2);

                // 2. Magnetic dock test
                DockResult dLeft = DrawerGeometryEngine.ComputeMagneticDock(6, 200, 64, 1920, 1080);
                DockResult dRight = DrawerGeometryEngine.ComputeMagneticDock(1920 - 64 - 8, 250, 64, 1920, 1080);
                DockResult dTop = DrawerGeometryEngine.ComputeMagneticDock(400, 10, 64, 1920, 1080);
                DockResult dFree = DrawerGeometryEngine.ComputeMagneticDock(300, 300, 64, 1920, 1080);
                bool magneticDockOk = (dLeft.DockedEdge == "left" && dLeft.X == 0 && dLeft.ForcedDirection == "right") &&
                                      (dRight.DockedEdge == "right" && dRight.X == 1920 - 64 && dRight.ForcedDirection == "left") &&
                                      (dTop.DockedEdge == "top" && dTop.Y == 0 && dTop.ForcedDirection == "down") &&
                                      (dFree.DockedEdge == "none" && dFree.X == 300 && dFree.Y == 300);

                // 3. Startup Registry test (using isolated test value name)
                string testValName = "Drawer_SelfTest_Probe";
                string fakeExe = @"C:\Users\Test\Drawer.exe";
                bool regWrite = StartupRegistryManager.SyncStartupValue(testValName, true, fakeExe);
                string readBack = StartupRegistryManager.GetStartupValue(testValName);
                bool regDel = StartupRegistryManager.SyncStartupValue(testValName, false, fakeExe);
                string readAfterDel = StartupRegistryManager.GetStartupValue(testValName);
                bool startupRegistryOk = regWrite &&
                                         readBack == "\"C:\\Users\\Test\\Drawer.exe\" --autostart" &&
                                         regDel &&
                                         readAfterDel == null;

                // 4. Shortcut resolver & Catalog Scanner test
                string tmpDir = Path.Combine(Path.GetTempPath(), "drawer-native-selftest-" + Guid.NewGuid().ToString("N").Substring(0, 6));
                Directory.CreateDirectory(tmpDir);
                string urlFile = Path.Combine(tmpDir, "CounterStrike2.url");
                File.WriteAllText(urlFile, "[InternetShortcut]\r\nURL=steam://rungameid/730\r\nIconFile=C:\\Windows\\explorer.exe\r\n");
                string uninstFile = Path.Combine(tmpDir, "Uninstall App.lnk");
                File.WriteAllText(uninstFile, "dummy");

                ResolvedShortcutInfo urlInfo = Win32ShellService.ResolveShortcutDetails(urlFile);
                bool shortcutResolverOk = (urlInfo.Url == "steam://rungameid/730");

                List<CatalogEntry> scanned = AppCatalogScanner.ScanDirectories(new[]
                {
                    Tuple.Create(tmpDir, "Área de Trabalho", 1)
                });
                bool catalogScannerOk = (scanned.Count == 1 && scanned[0].Name == "CounterStrike2");

                try { Directory.Delete(tmpDir, true); } catch { }

                // 5. Icon extraction test
                string explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                string dataUrl = Win32ShellService.ExtractHighResIconDataUrl(explorerPath);
                var bmpSrc = Win32ShellService.DataUrlToBitmapSource(dataUrl);
                bool iconExtractionOk = !string.IsNullOrEmpty(dataUrl) &&
                                        dataUrl.StartsWith("data:image/png;base64,") &&
                                        bmpSrc != null &&
                                        bmpSrc.PixelWidth >= 32;

                // 6. Drawer & Icon label sizing, non-overlapping panel placement, compact padding, and desktop positioner tests
                int wFerramentas = DrawerGeometryEngine.ComputeDrawerLabelWidth("Ferramentas", 48);
                int wEmuladores = DrawerGeometryEngine.ComputeDrawerLabelWidth("Emuladores", 48);
                int wIdes = DrawerGeometryEngine.ComputeDrawerLabelWidth("IDEs", 48);
                ScaleMetrics smallMetrics = DrawerGeometryEngine.GetScaleMetrics("small");

                bool labelSizingOk = (wFerramentas >= 84 && wFerramentas <= 110) &&
                                     (wEmuladores >= 80 && wEmuladores <= 105) &&
                                     (wIdes <= 50) &&
                                     (smallMetrics.CellWidth >= 70);

                PodPlacementResult pRight = DrawerGeometryEngine.ComputePodPanelPlacement(
                    16, 255, 48, "Ferramentas", "right", g3, 1920);
                PodPlacementResult pLeft = DrawerGeometryEngine.ComputePodPanelPlacement(
                    1850, 255, 48, "Ferramentas", "left", g3, 1920);
                bool noLabelPanelOverlapOk =
                    (pRight.PanelX >= pRight.LabelX + pRight.LabelWidth + DrawerGeometryEngine.PANEL_GAP_PX) &&
                    (pLeft.PanelX + g3.Width + DrawerGeometryEngine.PANEL_GAP_PX <= pLeft.LabelX);

                bool compactPanelPaddingOk =
                    (smallMetrics.Padding <= 6) &&
                    (smallMetrics.CellHeight <= 68);

                MethodInfo posMethod = typeof(Win32ShellService).GetMethod("PositionDesktopIconAtScreenPoint");
                bool desktopIconPositionerOk = (posMethod != null);

                // 7. Drag In -> Drag Out to Desktop -> Drag Back In cycle test
                string cycleRoot = Path.Combine(Path.GetTempPath(), "drawer-cycle-" + Guid.NewGuid().ToString("N").Substring(0, 6));
                string cycleStorage = Path.Combine(cycleRoot, "appdata");
                string cycleDesktop = Path.Combine(cycleRoot, "Desktop");
                Directory.CreateDirectory(cycleDesktop);
                string poeShortcut = Path.Combine(cycleDesktop, "Path of Exile.url");
                File.WriteAllText(poeShortcut, "[InternetShortcut]\r\nURL=steam://rungameid/238960\r\n");

                var cycleStore = new DrawerStateStore(cycleStorage, cycleDesktop);
                DrawerAppState cycleState = cycleStore.Load();
                string targetDrawerId = cycleState.drawers[0].id;

                DrawerItem added1 = cycleStore.AddItemToDrawer(targetDrawerId, poeShortcut, null);
                bool step1In = (added1 != null) && !File.Exists(poeShortcut) && File.Exists(added1.targetPath);

                string restoredPath = cycleStore.MoveItemToDesktop(targetDrawerId, added1.id);
                bool step2Out = (restoredPath == poeShortcut) && File.Exists(poeShortcut) && (cycleStore.GetDrawer(targetDrawerId).items.Count == 0);

                DrawerItem added2 = cycleStore.AddItemToDrawer(targetDrawerId, poeShortcut, null);
                bool step3BackIn = (added2 != null) && !File.Exists(poeShortcut) && File.Exists(added2.targetPath) && (cycleStore.GetDrawer(targetDrawerId).items.Count == 1);

                try { Directory.Delete(cycleRoot, true); } catch { }
                bool dragDropInOutCycleOk = step1In && step2Out && step3BackIn;

                bool authorCreditsAndDefaultStartupOk =
                    new DrawerSettings().launchAtStartup &&
                    AppCatalogWindow.AuthorSupportHeaderLine2.Contains("+55 21 985692631") &&
                    AppCatalogWindow.AuthorSupportHeaderLine3.Contains("Renan Santa Rosa Xavier - Mercado pago") &&
                    AppCatalogWindow.AuthorOfficialWarningText.Contains("AVISO: ESTE PROGRAMA FOI TOTALMENTE DESENVOLVIDO POR APENAS RENAN SANTA ROSA XAVIER");

                bool allOk = geometryOk && magneticDockOk && startupRegistryOk && shortcutResolverOk &&
                             iconExtractionOk && catalogScannerOk && labelSizingOk &&
                             noLabelPanelOverlapOk && compactPanelPaddingOk && desktopIconPositionerOk &&
                             dragDropInOutCycleOk && authorCreditsAndDefaultStartupOk;
                var result = new Dictionary<string, object>
                {
                    { "ok", allOk },
                    { "geometryOk", geometryOk },
                    { "magneticDockOk", magneticDockOk },
                    { "startupRegistryOk", startupRegistryOk },
                    { "shortcutResolverOk", shortcutResolverOk },
                    { "iconExtractionOk", iconExtractionOk },
                    { "catalogScannerOk", catalogScannerOk },
                    { "labelSizingOk", labelSizingOk },
                    { "noLabelPanelOverlapOk", noLabelPanelOverlapOk },
                    { "compactPanelPaddingOk", compactPanelPaddingOk },
                    { "desktopIconPositionerOk", desktopIconPositionerOk },
                    { "dragDropInOutCycleOk", dragDropInOutCycleOk },
                    { "authorCreditsAndDefaultStartupOk", authorCreditsAndDefaultStartupOk }
                };
                Console.Out.WriteLine(serializer.Serialize(result));
                return allOk ? 0 : 1;
            }
            catch (Exception ex)
            {
                var err = new Dictionary<string, object>
                {
                    { "ok", false },
                    { "error", ex.Message }
                };
                Console.Out.WriteLine(serializer.Serialize(err));
                return 1;
            }
        }

        public static int RunSmokeTest()
        {
            var serializer = new JavaScriptSerializer();
            string tmpDir = Path.Combine(Path.GetTempPath(), "drawer-smoke-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            try
            {
                Directory.CreateDirectory(tmpDir);
                string storageDir = Path.Combine(tmpDir, "appdata");
                string desktopDir = Path.Combine(tmpDir, "Desktop");
                Directory.CreateDirectory(desktopDir);

                var store = new DrawerStateStore(storageDir, desktopDir);
                var controller = new DrawerAppController(store);
                controller.Initialize(true);
                controller.CreateNewDrawer();

                int podsCreated = controller.ActivePodCount;
                bool hasTitleBar = false;
                bool isFullScreenOverlay = false;
                bool desktopLayerBottomLocked = true;
                bool usesWin32TrackPopupMenu = true;
                bool excludedFromShowDesktopPeek = true;

                var podList = new List<DrawerPodWindow>();
                foreach (DrawerPodWindow pod in controller.GetPodWindows())
                {
                    podList.Add(pod);
                    if (pod.WindowStyle != WindowStyle.None)
                        hasTitleBar = true;
                    if (pod.Width >= SystemParameters.WorkArea.Width * 0.8 &&
                        pod.Height >= SystemParameters.WorkArea.Height * 0.8)
                    {
                        isFullScreenOverlay = true;
                    }
                    if (!pod.IsBottomLayerLocked)
                        desktopLayerBottomLocked = false;
                    if (!pod.UsesWin32TrackPopupMenu)
                        usesWin32TrackPopupMenu = false;
                    if (!pod.IsExcludedFromShowDesktopPeek)
                        excludedFromShowDesktopPeek = false;
                }

                bool activePodAboveOtherPods = false;
                if (podList.Count >= 2)
                {
                    DrawerPodWindow podA = podList[0];
                    DrawerPodWindow podB = podList[1];
                    IntPtr hwndA = new WindowInteropHelper(podA).Handle;
                    IntPtr hwndB = new WindowInteropHelper(podB).Handle;

                    controller.ActivatePod(podA.DrawerId, true);
                    bool aAboveB = Win32ShellService.IsWindowAboveInZOrder(hwndA, hwndB);

                    controller.ActivatePod(podB.DrawerId, true);
                    bool bAboveA = Win32ShellService.IsWindowAboveInZOrder(hwndB, hwndA);

                    activePodAboveOtherPods = aAboveB && bAboveA;
                }

                controller.Shutdown();
                try { Directory.Delete(tmpDir, true); } catch { }

                bool ok = (podsCreated >= 2) &&
                          !hasTitleBar &&
                          !isFullScreenOverlay &&
                          desktopLayerBottomLocked &&
                          usesWin32TrackPopupMenu &&
                          excludedFromShowDesktopPeek &&
                          activePodAboveOtherPods;
                var report = new Dictionary<string, object>
                {
                    { "ok", ok },
                    { "podsCreated", podsCreated },
                    { "hasTitleBar", hasTitleBar },
                    { "isFullScreenOverlay", isFullScreenOverlay },
                    { "desktopLayerBottomLocked", desktopLayerBottomLocked },
                    { "usesWin32TrackPopupMenu", usesWin32TrackPopupMenu },
                    { "excludedFromShowDesktopPeek", excludedFromShowDesktopPeek },
                    { "activePodAboveOtherPods", activePodAboveOtherPods }
                };
                Console.Out.WriteLine(serializer.Serialize(report));
                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                try { Directory.Delete(tmpDir, true); } catch { }
                var err = new Dictionary<string, object>
                {
                    { "ok", false },
                    { "error", ex.Message }
                };
                Console.Out.WriteLine(serializer.Serialize(err));
                return 1;
            }
        }
    }

    public class DrawerProgram
    {
        private static Mutex _singleInstanceMutex;

        [STAThread]
        public static int Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                string cmd = args[0];

                if (cmd == "--self-test")
                {
                    return DrawerSelfTestRunner.RunSelfTest();
                }

                if (cmd == "--smoke-test")
                {
                    return DrawerSelfTestRunner.RunSmokeTest();
                }

                // Backwards-compatible CLI commands for helper / tests
                if (cmd == "extract-icon" && args.Length >= 2)
                {
                    string dataUrl = Win32ShellService.ExtractHighResIconDataUrl(args[1]);
                    Console.Out.Write(dataUrl);
                    return 0;
                }

                if (cmd == "notify-desktop-file" && args.Length >= 2)
                {
                    Win32ShellService.NotifyDesktopFileRestored(args[1]);
                    return 0;
                }
            }

            // CRITICAL FOR WINDOWS DRAG & DROP:
            // If Drawer.exe was launched from an elevated (Administrator) process while Windows Explorer (Desktop)
            // runs at Medium Integrity, re-launch using explorer.exe's Medium-Integrity token so UIPI never blocks Drag & Drop!
            bool alreadyDeElevated = false;
            if (args != null)
            {
                foreach (string a in args)
                {
                    if (a == "--unelevated") alreadyDeElevated = true;
                }
            }
            if (!alreadyDeElevated && Win32ShellService.IsCurrentProcessElevated())
            {
                try
                {
                    string exePath = Process.GetCurrentProcess().MainModule.FileName;
                    if (Win32ShellService.TryRelaunchUnelevatedViaExplorer(exePath, "--unelevated"))
                    {
                        return 0;
                    }
                }
                catch { }
            }

            bool createdNew;
            _singleInstanceMutex = new Mutex(true, "Local\\Drawer2_NativePods_Mutex", out createdNew);
            if (!createdNew)
            {
                return 0;
            }

            try
            {
                var app = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };

                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Drawer");
                string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                var store = new DrawerStateStore(appDataDir, desktopDir);
                var controller = new DrawerAppController(store);

                app.Startup += delegate
                {
                    controller.Initialize(false);
                };

                app.Run();
                return 0;
            }
            finally
            {
                if (_singleInstanceMutex != null)
                {
                    try { _singleInstanceMutex.ReleaseMutex(); } catch { }
                    _singleInstanceMutex.Dispose();
                }
            }
        }
    }
}
