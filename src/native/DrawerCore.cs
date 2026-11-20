using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace DrawerNative
{
    public class DrawerSettings
    {
        public string iconScaleMode { get; set; }
        public string backgroundMode { get; set; }
        public bool launchAtStartup { get; set; }
        public bool moveDesktopFilesToVault { get; set; }

        public DrawerSettings()
        {
            iconScaleMode = "auto";
            backgroundMode = "blue";
            launchAtStartup = true;
            moveDesktopFilesToVault = true;
        }
    }

    public class DrawerItem
    {
        public string id { get; set; }
        public string name { get; set; }
        public string targetPath { get; set; }
        public string originalDesktopPath { get; set; }
        public bool vaulted { get; set; }
        public string iconDataUrl { get; set; }

        public DrawerItem()
        {
            id = "item-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            name = "Atalho";
            targetPath = "";
            originalDesktopPath = null;
            vaulted = false;
            iconDataUrl = "";
        }
    }

    public class DrawerModel
    {
        public string id { get; set; }
        public string name { get; set; }
        public int x { get; set; }
        public int y { get; set; }
        public string preferredDirection { get; set; }
        public string dockedEdge { get; set; }
        public List<DrawerItem> items { get; set; }

        public DrawerModel()
        {
            id = "drawer-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            name = "Nova Gaveta";
            x = 36;
            y = 60;
            preferredDirection = "auto";
            dockedEdge = "none";
            items = new List<DrawerItem>();
        }
    }

    public class DrawerAppState
    {
        public DrawerSettings settings { get; set; }
        public List<DrawerModel> drawers { get; set; }

        public DrawerAppState()
        {
            settings = new DrawerSettings();
            drawers = new List<DrawerModel>();
        }
    }

    public class ScaleMetrics
    {
        public string Scale;
        public int DrawerSize;
        public int IconSize;
        public int CellWidth;
        public int CellHeight;
        public int BorderRadius;
        public int PreviewIconSize;
        public int Padding;
        public int Gap;
    }

    public class GridDimensions
    {
        public int Cols;
        public int Rows;
        public int Width;
        public int Height;
        public ScaleMetrics Metrics;
    }

    public class DockResult
    {
        public int X;
        public int Y;
        public string DockedEdge; // "none" | "left" | "right" | "top"
        public string ForcedDirection; // null | "right" | "left" | "down"
    }

    public class PodPlacementResult
    {
        public int HandleX;
        public int HandleY;
        public int HandleSize;
        public int LabelX;
        public int LabelY;
        public int LabelWidth;
        public int LabelHeight;
        public int PanelX;
        public int PanelY;
        public int PanelWidth;
        public int PanelHeight;
    }

    public static class DrawerGeometryEngine
    {
        public const int SNAP_THRESHOLD_PX = 18;
        public const int LABEL_HEIGHT_PX = 26;
        public const int PANEL_GAP_PX = 6;
        public const int EDGE_TAB_THICKNESS_PX = 14;

        public static int ComputeDrawerLabelWidth(string drawerName, int handleWidth)
        {
            int len = string.IsNullOrEmpty(drawerName) ? 4 : drawerName.Trim().Length;
            int needed = (int)Math.Ceiling(len * 6.8) + 16;
            return Math.Max(42, Math.Min(164, needed));
        }

        public static PodPlacementResult ComputePodPanelPlacement(
            int handleX,
            int handleY,
            int handleSize,
            string drawerName,
            string direction,
            GridDimensions grid,
            int screenW)
        {
            int labelW = ComputeDrawerLabelWidth(drawerName, handleSize);
            int labelX = Math.Max(4, Math.Min(screenW - labelW - 4, handleX + (handleSize - labelW) / 2));
            int labelY = handleY + handleSize + 3;

            int panelX = handleX;
            int panelY = handleY;

            if (direction == "right")
            {
                int rightMost = Math.Max(handleX + handleSize, labelX + labelW);
                panelX = rightMost + PANEL_GAP_PX;
                panelY = handleY;
            }
            else if (direction == "left")
            {
                int leftMost = Math.Min(handleX, labelX);
                panelX = leftMost - PANEL_GAP_PX - grid.Width;
                panelY = handleY;
            }
            else
            {
                // down
                panelX = handleX;
                panelY = handleY + handleSize + LABEL_HEIGHT_PX + 2;
            }

            return new PodPlacementResult
            {
                HandleX = handleX,
                HandleY = handleY,
                HandleSize = handleSize,
                LabelX = labelX,
                LabelY = labelY,
                LabelWidth = labelW,
                LabelHeight = LABEL_HEIGHT_PX,
                PanelX = panelX,
                PanelY = panelY,
                PanelWidth = grid.Width,
                PanelHeight = grid.Height
            };
        }

        public static ScaleMetrics GetScaleMetrics(string scale)
        {
            if (scale == "small")
            {
                return new ScaleMetrics
                {
                    Scale = "small",
                    DrawerSize = 48,
                    IconSize = 32,
                    CellWidth = 74,
                    CellHeight = 64,
                    BorderRadius = 12,
                    PreviewIconSize = 16,
                    Padding = 5,
                    Gap = 4
                };
            }
            if (scale == "large")
            {
                return new ScaleMetrics
                {
                    Scale = "large",
                    DrawerSize = 88,
                    IconSize = 56,
                    CellWidth = 98,
                    CellHeight = 90,
                    BorderRadius = 20,
                    PreviewIconSize = 30,
                    Padding = 7,
                    Gap = 6
                };
            }
            return new ScaleMetrics
            {
                Scale = "medium",
                DrawerSize = 64,
                IconSize = 44,
                CellWidth = 84,
                CellHeight = 76,
                BorderRadius = 15,
                PreviewIconSize = 22,
                Padding = 6,
                Gap = 5
            };
        }

        public static GridDimensions ComputeDrawerGrid(int iconCount, string direction, string scale)
        {
            ScaleMetrics m = GetScaleMetrics(scale);
            int count = Math.Max(1, iconCount);
            int maxMainAxis = 4;
            int cols;
            int rows;

            if (direction == "down")
            {
                rows = Math.Min(count, maxMainAxis);
                cols = (int)Math.Ceiling((double)count / maxMainAxis);
            }
            else
            {
                cols = Math.Min(count, maxMainAxis);
                rows = (int)Math.Ceiling((double)count / maxMainAxis);
            }

            int width = m.Padding * 2 + cols * m.CellWidth + Math.Max(0, cols - 1) * m.Gap;
            int height = m.Padding * 2 + rows * m.CellHeight + Math.Max(0, rows - 1) * m.Gap;

            return new GridDimensions
            {
                Cols = cols,
                Rows = rows,
                Width = width,
                Height = height,
                Metrics = m
            };
        }

        public static DockResult ComputeMagneticDock(int x, int y, int drawerSize, int screenWidth, int screenHeight)
        {
            int minMargin = 12;
            int maxX = Math.Max(minMargin, screenWidth - drawerSize - minMargin);
            int maxY = Math.Max(minMargin, screenHeight - drawerSize - LABEL_HEIGHT_PX);

            if (x <= SNAP_THRESHOLD_PX)
            {
                return new DockResult
                {
                    X = 0,
                    Y = Math.Min(Math.Max(minMargin, y), maxY),
                    DockedEdge = "left",
                    ForcedDirection = "right"
                };
            }

            if (x + drawerSize >= screenWidth - SNAP_THRESHOLD_PX)
            {
                return new DockResult
                {
                    X = Math.Max(0, screenWidth - drawerSize),
                    Y = Math.Min(Math.Max(minMargin, y), maxY),
                    DockedEdge = "right",
                    ForcedDirection = "left"
                };
            }

            if (y <= SNAP_THRESHOLD_PX)
            {
                return new DockResult
                {
                    X = Math.Min(Math.Max(minMargin, x), maxX),
                    Y = 0,
                    DockedEdge = "top",
                    ForcedDirection = "down"
                };
            }

            return new DockResult
            {
                X = Math.Min(Math.Max(minMargin, x), maxX),
                Y = Math.Min(Math.Max(minMargin, y), maxY),
                DockedEdge = "none",
                ForcedDirection = null
            };
        }

        public static string ComputeOpenDirection(
            int x,
            int y,
            int iconCount,
            string scale,
            int screenWidth,
            int screenHeight,
            string preferredDirection,
            string dockedEdge)
        {
            if (dockedEdge == "left") return "right";
            if (dockedEdge == "right") return "left";
            if (dockedEdge == "top") return "down";

            ScaleMetrics m = GetScaleMetrics(scale);
            int drawerSize = m.DrawerSize;
            int margin = 16;

            Func<string, bool> fits = delegate(string dir)
            {
                GridDimensions g = ComputeDrawerGrid(iconCount, dir, scale);
                if (dir == "right")
                {
                    return x + drawerSize + PANEL_GAP_PX + g.Width + margin <= screenWidth;
                }
                if (dir == "left")
                {
                    return x - PANEL_GAP_PX - g.Width >= margin;
                }
                if (dir == "down")
                {
                    return y + drawerSize + LABEL_HEIGHT_PX + PANEL_GAP_PX + g.Height + margin <= screenHeight;
                }
                return false;
            };

            if (!string.IsNullOrEmpty(preferredDirection) &&
                preferredDirection != "auto" &&
                (preferredDirection == "right" || preferredDirection == "down" || preferredDirection == "left"))
            {
                if (fits(preferredDirection))
                {
                    return preferredDirection;
                }
            }

            string[] candidates = (x > screenWidth * 0.65)
                ? new string[] { "right", "left", "down" }
                : new string[] { "right", "down", "left" };

            foreach (string dir in candidates)
            {
                if (fits(dir)) return dir;
            }

            int roomRight = screenWidth - (x + drawerSize);
            int roomLeft = x;
            int roomDown = screenHeight - (y + drawerSize);
            if (roomRight >= roomLeft && roomRight >= roomDown) return "right";
            if (roomLeft >= roomRight && roomLeft >= roomDown) return "left";
            return "down";
        }
    }

    public static class StartupRegistryManager
    {
        public const string RUN_KEY_PATH = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string DEFAULT_VALUE_NAME = "Drawer";

        public static string BuildStartupCommand(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath)) return "";
            string clean = exePath.Trim().Trim('"');
            return string.Format("\"{0}\" --autostart", clean);
        }

        public static bool SyncStartup(bool enabled, string exePath)
        {
            return SyncStartupValue(DEFAULT_VALUE_NAME, enabled, exePath);
        }

        public static bool SyncStartupValue(string valueName, bool enabled, string exePath)
        {
            if (string.IsNullOrWhiteSpace(valueName)) return false;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_KEY_PATH, true))
                {
                    if (key == null) return false;
                    if (enabled)
                    {
                        string cmd = BuildStartupCommand(exePath);
                        if (string.IsNullOrEmpty(cmd)) return false;
                        key.SetValue(valueName, cmd, RegistryValueKind.String);
                    }
                    else
                    {
                        if (key.GetValue(valueName) != null)
                        {
                            key.DeleteValue(valueName, false);
                        }
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string GetStartupValue(string valueName)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_KEY_PATH, false))
                {
                    if (key == null) return null;
                    object val = key.GetValue(valueName);
                    return val != null ? val.ToString() : null;
                }
            }
            catch
            {
                return null;
            }
        }
    }

    public static class WindowsScaleDetector
    {
        public static string ReadDesktopIconScale()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Bags\1\Desktop", false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("IconSize");
                        if (val != null)
                        {
                            int size = Convert.ToInt32(val);
                            if (size <= 36) return "small";
                            if (size <= 56) return "medium";
                            return "large";
                        }
                    }
                }
            }
            catch
            {
                // Ignore
            }
            return "medium";
        }
    }

    public class CatalogEntry
    {
        public string Name { get; set; }
        public string FilePath { get; set; }
        public string SourceLabel { get; set; }
        public string Extension { get; set; }
    }

    public static class AppCatalogScanner
    {
        private static readonly Regex UninstallRegex = new Regex(
            @"^(uninstall|desinstalar)\b|\b(uninstall|desinstalar)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static List<CatalogEntry> ScanDefaultSystemShortcuts()
        {
            var roots = new List<Tuple<string, string, int>>();
            string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            string userPrograms = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            string commonPrograms = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);

            if (!string.IsNullOrEmpty(userDesktop))
                roots.Add(Tuple.Create(userDesktop, "Área de Trabalho", 1));
            if (!string.IsNullOrEmpty(commonDesktop))
                roots.Add(Tuple.Create(commonDesktop, "Área de Trabalho (Pública)", 1));
            if (!string.IsNullOrEmpty(userPrograms))
                roots.Add(Tuple.Create(userPrograms, "Menu Iniciar", 3));
            if (!string.IsNullOrEmpty(commonPrograms))
                roots.Add(Tuple.Create(commonPrograms, "Menu Iniciar (Sistema)", 3));

            return ScanDirectories(roots);
        }

        public static List<CatalogEntry> ScanDirectories(IEnumerable<Tuple<string, string, int>> roots)
        {
            var list = new List<CatalogEntry>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in roots)
            {
                if (root == null || string.IsNullOrEmpty(root.Item1)) continue;
                WalkDir(root.Item1, root.Item2, 1, root.Item3, list, seenNames);
            }

            list.Sort(delegate(CatalogEntry a, CatalogEntry b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
        }

        private static void WalkDir(
            string dirPath,
            string sourceLabel,
            int depth,
            int maxDepth,
            List<CatalogEntry> results,
            HashSet<string> seenNames)
        {
            if (depth > maxDepth || !Directory.Exists(dirPath)) return;

            try
            {
                foreach (string file in Directory.GetFiles(dirPath))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext != ".lnk" && ext != ".url" && ext != ".exe") continue;

                    string baseName = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrWhiteSpace(baseName)) continue;
                    baseName = baseName.Trim();

                    if (UninstallRegex.IsMatch(baseName)) continue;
                    if (baseName.Equals("desktop", StringComparison.OrdinalIgnoreCase)) continue;
                    if (seenNames.Contains(baseName)) continue;

                    seenNames.Add(baseName);
                    results.Add(new CatalogEntry
                    {
                        Name = baseName,
                        FilePath = file,
                        SourceLabel = sourceLabel,
                        Extension = ext
                    });
                }

                if (depth < maxDepth)
                {
                    foreach (string subDir in Directory.GetDirectories(dirPath))
                    {
                        WalkDir(subDir, sourceLabel, depth + 1, maxDepth, results, seenNames);
                    }
                }
            }
            catch
            {
                // Ignore inaccessible system folders
            }
        }
    }

    public class DrawerStateStore
    {
        private readonly string _storageDir;
        private readonly string _stateFilePath;
        private readonly string _vaultRootDir;
        private readonly string _desktopDir;
        private readonly JavaScriptSerializer _serializer;
        private DrawerAppState _state;

        public DrawerStateStore(string storageDir, string desktopDir)
        {
            _storageDir = storageDir;
            _desktopDir = desktopDir;
            _stateFilePath = Path.Combine(_storageDir, "drawers-state.json");
            _vaultRootDir = Path.Combine(_storageDir, "vault");
            _serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

            Directory.CreateDirectory(_storageDir);
            Directory.CreateDirectory(_vaultRootDir);
        }

        public static string NormalizeName(string raw, string fallback)
        {
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            string trimmed = raw.Trim();
            return trimmed.Length > 40 ? trimmed.Substring(0, 40) : trimmed;
        }

        public DrawerAppState GetState()
        {
            if (_state == null) Load();
            return _state;
        }

        public string GetEffectiveScale()
        {
            DrawerAppState st = GetState();
            if (st.settings.iconScaleMode == "small" ||
                st.settings.iconScaleMode == "medium" ||
                st.settings.iconScaleMode == "large")
            {
                return st.settings.iconScaleMode;
            }
            return WindowsScaleDetector.ReadDesktopIconScale();
        }

        public DrawerAppState CreateDefaultState()
        {
            var st = new DrawerAppState();
            st.drawers.Add(new DrawerModel
            {
                id = "drawer-default-1",
                name = "Principais",
                x = 36,
                y = 60,
                preferredDirection = "auto",
                dockedEdge = "none"
            });
            st.drawers.Add(new DrawerModel
            {
                id = "drawer-default-2",
                name = "Jogos & Apps",
                x = 36,
                y = 185,
                preferredDirection = "auto",
                dockedEdge = "none"
            });
            return st;
        }

        public DrawerAppState Load()
        {
            if (File.Exists(_stateFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_stateFilePath, Encoding.UTF8);
                    DrawerAppState parsed = _serializer.Deserialize<DrawerAppState>(json);
                    if (parsed != null)
                    {
                        if (parsed.settings == null) parsed.settings = new DrawerSettings();
                        if (parsed.settings.backgroundMode != "transparent")
                            parsed.settings.backgroundMode = "blue";
                        if (string.IsNullOrEmpty(parsed.settings.iconScaleMode))
                            parsed.settings.iconScaleMode = "auto";
                        if (parsed.drawers == null)
                            parsed.drawers = new List<DrawerModel>();

                        foreach (DrawerModel d in parsed.drawers)
                        {
                            if (string.IsNullOrEmpty(d.id))
                                d.id = "drawer-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                            d.name = NormalizeName(d.name, "Nova Gaveta");
                            if (string.IsNullOrEmpty(d.preferredDirection))
                                d.preferredDirection = "auto";
                            if (string.IsNullOrEmpty(d.dockedEdge))
                                d.dockedEdge = "none";
                            if (d.items == null)
                                d.items = new List<DrawerItem>();
                        }

                        _state = parsed;
                        SyncVaultWithSettings();
                        return _state;
                    }
                }
                catch
                {
                    // Fallback to default
                }
            }

            _state = CreateDefaultState();
            Save();
            return _state;
        }

        public void Save()
        {
            if (_state == null) _state = CreateDefaultState();
            Directory.CreateDirectory(_storageDir);
            string json = _serializer.Serialize(_state);
            File.WriteAllText(_stateFilePath, json, Encoding.UTF8);
        }

        public DrawerModel GetDrawer(string drawerId)
        {
            foreach (DrawerModel d in GetState().drawers)
            {
                if (d.id == drawerId) return d;
            }
            return null;
        }

        public DrawerModel CreateDrawer(string name, int x, int y, string dockedEdge)
        {
            DrawerAppState st = GetState();
            var d = new DrawerModel
            {
                id = "drawer-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                name = NormalizeName(name, string.Format("Gaveta {0}", st.drawers.Count + 1)),
                x = x,
                y = y,
                preferredDirection = "auto",
                dockedEdge = string.IsNullOrEmpty(dockedEdge) ? "none" : dockedEdge,
                items = new List<DrawerItem>()
            };
            st.drawers.Add(d);
            Save();
            return d;
        }

        public DrawerModel RenameDrawer(string drawerId, string newName)
        {
            DrawerModel d = GetDrawer(drawerId);
            if (d == null) return null;
            d.name = NormalizeName(newName, "Nova Gaveta");
            Save();
            return d;
        }

        public DrawerModel MoveDrawer(string drawerId, int x, int y, int drawerSize, int screenWidth, int screenHeight)
        {
            DrawerModel d = GetDrawer(drawerId);
            if (d == null) return null;

            DockResult dock = DrawerGeometryEngine.ComputeMagneticDock(x, y, drawerSize, screenWidth, screenHeight);
            d.x = dock.X;
            d.y = dock.Y;
            d.dockedEdge = dock.DockedEdge;
            Save();
            return d;
        }

        public DrawerModel SetDrawerDirection(string drawerId, string preferredDirection)
        {
            DrawerModel d = GetDrawer(drawerId);
            if (d == null) return null;
            if (preferredDirection == "auto" || preferredDirection == "right" || preferredDirection == "down" || preferredDirection == "left")
            {
                d.preferredDirection = preferredDirection;
                Save();
            }
            return d;
        }

        public DrawerModel SetDrawerDockedEdge(string drawerId, string edge, int drawerSize, int screenWidth, int screenHeight)
        {
            DrawerModel d = GetDrawer(drawerId);
            if (d == null) return null;

            if (edge == "left")
            {
                d.x = 0;
                d.dockedEdge = "left";
            }
            else if (edge == "right")
            {
                d.x = Math.Max(0, screenWidth - drawerSize);
                d.dockedEdge = "right";
            }
            else if (edge == "top")
            {
                d.y = 0;
                d.dockedEdge = "top";
            }
            else
            {
                d.dockedEdge = "none";
                if (d.x <= DrawerGeometryEngine.SNAP_THRESHOLD_PX) d.x = 36;
                if (d.x + drawerSize >= screenWidth - DrawerGeometryEngine.SNAP_THRESHOLD_PX)
                    d.x = Math.Max(36, screenWidth - drawerSize - 48);
                if (d.y <= DrawerGeometryEngine.SNAP_THRESHOLD_PX) d.y = 60;
            }
            Save();
            return d;
        }

        private static bool IsInsideDirectory(string filePath, string parentDir)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(parentDir)) return false;
            try
            {
                string fullFile = Path.GetFullPath(filePath).TrimEnd('\\', '/');
                string fullParent = Path.GetFullPath(parentDir).TrimEnd('\\', '/') + "\\";
                return fullFile.StartsWith(fullParent, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool IsDesktopFilePath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            if (!string.IsNullOrEmpty(_desktopDir) && IsInsideDirectory(filePath, _desktopDir))
                return true;

            try
            {
                string realUserDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (!string.IsNullOrEmpty(_desktopDir) &&
                    !string.IsNullOrEmpty(realUserDesktop) &&
                    string.Equals(
                        Path.GetFullPath(_desktopDir).TrimEnd('\\', '/'),
                        Path.GetFullPath(realUserDesktop).TrimEnd('\\', '/'),
                        StringComparison.OrdinalIgnoreCase))
                {
                    string commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                    if (!string.IsNullOrEmpty(commonDesktop) && IsInsideDirectory(filePath, commonDesktop))
                        return true;
                }
            }
            catch { }

            return false;
        }

        private static void MoveFileSafe(string src, string dest)
        {
            string dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(dest))
            {
                try { File.Delete(dest); } catch { }
            }
            try
            {
                File.Move(src, dest);
            }
            catch
            {
                File.Copy(src, dest, true);
                File.Delete(src);
            }
        }

        public string RestoreItemFileIfVaulted(DrawerItem item)
        {
            if (item == null) return null;

            bool inVault = item.vaulted || (!string.IsNullOrEmpty(item.targetPath) && IsInsideDirectory(item.targetPath, _vaultRootDir));
            if (inVault && !string.IsNullOrEmpty(item.targetPath) && File.Exists(item.targetPath))
            {
                string restoreDest = !string.IsNullOrEmpty(item.originalDesktopPath)
                    ? item.originalDesktopPath
                    : (!string.IsNullOrEmpty(_desktopDir) ? Path.Combine(_desktopDir, Path.GetFileName(item.targetPath)) : null);

                if (!string.IsNullOrEmpty(restoreDest))
                {
                    MoveFileSafe(item.targetPath, restoreDest);
                    Win32ShellService.NotifyDesktopFileRestored(restoreDest);
                    return restoreDest;
                }
            }

            if (!string.IsNullOrEmpty(_desktopDir) &&
                !string.IsNullOrEmpty(item.targetPath) &&
                File.Exists(item.targetPath) &&
                !IsInsideDirectory(item.targetPath, _desktopDir))
            {
                string ext = Path.GetExtension(item.targetPath).ToLowerInvariant();
                if (ext == ".lnk" || ext == ".url")
                {
                    string destOnDesktop = !string.IsNullOrEmpty(item.originalDesktopPath)
                        ? item.originalDesktopPath
                        : Path.Combine(_desktopDir, Path.GetFileName(item.targetPath));
                    if (!File.Exists(destOnDesktop))
                    {
                        try
                        {
                            File.Copy(item.targetPath, destOnDesktop, false);
                            Win32ShellService.NotifyDesktopFileRestored(destOnDesktop);
                            return destOnDesktop;
                        }
                        catch
                        {
                            // Fallback
                        }
                    }
                    return destOnDesktop;
                }
            }

            return !string.IsNullOrEmpty(item.originalDesktopPath) ? item.originalDesktopPath : item.targetPath;
        }

        public void SyncVaultWithSettings()
        {
            if (_state == null || string.IsNullOrEmpty(_desktopDir)) return;
            bool shouldVault = _state.settings.moveDesktopFilesToVault;
            bool changed = false;

            foreach (DrawerModel drawer in _state.drawers)
            {
                foreach (DrawerItem item in drawer.items)
                {
                    if (shouldVault)
                    {
                        if (!item.vaulted &&
                            !string.IsNullOrEmpty(item.targetPath) &&
                            File.Exists(item.targetPath) &&
                            IsDesktopFilePath(item.targetPath))
                        {
                            string itemVaultDir = Path.Combine(_vaultRootDir, drawer.id);
                            Directory.CreateDirectory(itemVaultDir);
                            string destPath = Path.Combine(itemVaultDir, Path.GetFileName(item.targetPath));
                            string origPath = item.targetPath;
                            MoveFileSafe(origPath, destPath);
                            item.originalDesktopPath = origPath;
                            item.targetPath = destPath;
                            item.vaulted = true;
                            changed = true;
                            Win32ShellService.NotifyDesktopFileRemoved(origPath);
                        }
                        else if (item.vaulted && !string.IsNullOrEmpty(item.originalDesktopPath))
                        {
                            if (File.Exists(item.originalDesktopPath) && IsDesktopFilePath(item.originalDesktopPath))
                            {
                                string itemVaultDir = Path.Combine(_vaultRootDir, drawer.id);
                                Directory.CreateDirectory(itemVaultDir);
                                string destPath = !string.IsNullOrEmpty(item.targetPath)
                                    ? item.targetPath
                                    : Path.Combine(itemVaultDir, Path.GetFileName(item.originalDesktopPath));
                                try
                                {
                                    MoveFileSafe(item.originalDesktopPath, destPath);
                                    item.targetPath = destPath;
                                    changed = true;
                                }
                                catch { }
                            }
                            Win32ShellService.NotifyDesktopFileRemoved(item.originalDesktopPath);
                        }
                    }
                    else if (item.vaulted && !string.IsNullOrEmpty(item.targetPath) && File.Exists(item.targetPath))
                    {
                        string restored = RestoreItemFileIfVaulted(item);
                        if (!string.IsNullOrEmpty(restored))
                        {
                            item.targetPath = restored;
                            item.vaulted = false;
                            changed = true;
                        }
                    }
                }
            }

            if (changed) Save();
        }

        public DrawerItem AddItemToDrawer(string drawerId, string filePath, string customName)
        {
            DrawerModel drawer = GetDrawer(drawerId);
            if (drawer == null || string.IsNullOrWhiteSpace(filePath)) return null;
            if (!File.Exists(filePath) && !Directory.Exists(filePath)) return null;

            foreach (DrawerItem existing in drawer.items)
            {
                if (string.Equals(existing.targetPath, filePath, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(existing.originalDesktopPath) &&
                     string.Equals(existing.originalDesktopPath, filePath, StringComparison.OrdinalIgnoreCase)))
                {
                    if (GetState().settings.moveDesktopFilesToVault &&
                        File.Exists(filePath) &&
                        IsDesktopFilePath(filePath))
                    {
                        try
                        {
                            string itemVaultDir = Path.Combine(_vaultRootDir, drawer.id);
                            Directory.CreateDirectory(itemVaultDir);
                            string destPath = Path.Combine(itemVaultDir, Path.GetFileName(filePath));
                            MoveFileSafe(filePath, destPath);
                            existing.targetPath = destPath;
                            existing.originalDesktopPath = filePath;
                            existing.vaulted = true;
                            Save();
                            Win32ShellService.NotifyDesktopFileRemoved(filePath);
                        }
                        catch { }
                    }
                    return existing;
                }
            }

            string baseName = !string.IsNullOrWhiteSpace(customName)
                ? customName
                : Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = Path.GetFileName(filePath);

            // Extract icon BEFORE moving file so relative shell icon references work cleanly
            string iconDataUrl = Win32ShellService.ExtractHighResIconDataUrl(filePath);

            string finalTargetPath = filePath;
            string originalDesktopPath = null;
            bool vaulted = false;

            if (GetState().settings.moveDesktopFilesToVault &&
                !string.IsNullOrEmpty(_desktopDir) &&
                File.Exists(filePath) &&
                IsDesktopFilePath(filePath))
            {
                try
                {
                    string itemVaultDir = Path.Combine(_vaultRootDir, drawer.id);
                    Directory.CreateDirectory(itemVaultDir);
                    string destPath = Path.Combine(itemVaultDir, Path.GetFileName(filePath));
                    MoveFileSafe(filePath, destPath);
                    finalTargetPath = destPath;
                    originalDesktopPath = filePath;
                    vaulted = true;
                    Win32ShellService.NotifyDesktopFileRemoved(filePath);
                }
                catch
                {
                    finalTargetPath = filePath;
                    vaulted = false;
                }
            }

            var newItem = new DrawerItem
            {
                id = "item-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                name = NormalizeName(baseName, "Atalho"),
                targetPath = finalTargetPath,
                originalDesktopPath = originalDesktopPath,
                vaulted = vaulted,
                iconDataUrl = iconDataUrl
            };

            drawer.items.Add(newItem);
            Save();
            return newItem;
        }

        public string MoveItemToDesktop(string drawerId, string itemId)
        {
            DrawerModel drawer = GetDrawer(drawerId);
            if (drawer == null) return null;

            DrawerItem found = null;
            for (int i = 0; i < drawer.items.Count; i++)
            {
                if (drawer.items[i].id == itemId)
                {
                    found = drawer.items[i];
                    drawer.items.RemoveAt(i);
                    break;
                }
            }
            if (found == null) return null;

            string restored = RestoreItemFileIfVaulted(found);
            Save();
            return restored;
        }

        public DrawerItem MoveItemBetweenDrawers(string fromDrawerId, string itemId, string toDrawerId)
        {
            if (string.IsNullOrEmpty(fromDrawerId) || string.IsNullOrEmpty(toDrawerId) || fromDrawerId == toDrawerId)
                return null;

            DrawerModel fromDrawer = GetDrawer(fromDrawerId);
            DrawerModel toDrawer = GetDrawer(toDrawerId);
            if (fromDrawer == null || toDrawer == null) return null;

            DrawerItem item = null;
            for (int i = 0; i < fromDrawer.items.Count; i++)
            {
                if (fromDrawer.items[i].id == itemId)
                {
                    item = fromDrawer.items[i];
                    fromDrawer.items.RemoveAt(i);
                    break;
                }
            }
            if (item == null) return null;

            bool inVault = item.vaulted || (!string.IsNullOrEmpty(item.targetPath) && IsInsideDirectory(item.targetPath, _vaultRootDir));
            if (inVault && !string.IsNullOrEmpty(item.targetPath) && File.Exists(item.targetPath))
            {
                string targetVaultDir = Path.Combine(_vaultRootDir, toDrawer.id);
                Directory.CreateDirectory(targetVaultDir);
                string newVaultPath = Path.Combine(targetVaultDir, Path.GetFileName(item.targetPath));
                if (!string.Equals(Path.GetFullPath(item.targetPath), Path.GetFullPath(newVaultPath), StringComparison.OrdinalIgnoreCase))
                {
                    MoveFileSafe(item.targetPath, newVaultPath);
                    item.targetPath = newVaultPath;
                }
            }

            toDrawer.items.Add(item);
            Save();
            return item;
        }

        public DrawerItem RenameItem(string drawerId, string itemId, string newName)
        {
            DrawerModel drawer = GetDrawer(drawerId);
            if (drawer == null) return null;
            foreach (DrawerItem it in drawer.items)
            {
                if (it.id == itemId)
                {
                    it.name = NormalizeName(newName, "Atalho");
                    Save();
                    return it;
                }
            }
            return null;
        }

        public bool DeleteDrawer(string drawerId)
        {
            DrawerAppState st = GetState();
            for (int i = 0; i < st.drawers.Count; i++)
            {
                if (st.drawers[i].id == drawerId)
                {
                    DrawerModel removed = st.drawers[i];
                    st.drawers.RemoveAt(i);
                    foreach (DrawerItem it in removed.items)
                    {
                        RestoreItemFileIfVaulted(it);
                    }
                    Save();
                    return true;
                }
            }
            return false;
        }
    }
}
