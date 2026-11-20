using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace DrawerInstallerApp
{
    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLinkCoClass
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    internal interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    public static class InstallerEngine
    {
        public static string GetDefaultInstallDir()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "Drawer");
        }

        public static void StopExistingDrawerProcesses()
        {
            int selfPid = Process.GetCurrentProcess().Id;
            string[] names = new string[] { "Drawer", "win32-helper" };
            foreach (string name in names)
            {
                try
                {
                    foreach (Process p in Process.GetProcessesByName(name))
                    {
                        if (p.Id == selfPid) continue;
                        try
                        {
                            p.Kill();
                            p.WaitForExit(2000);
                        }
                        catch { }
                    }
                }
                catch { }
            }
            Thread.Sleep(250);
        }

        private static void CleanupLegacyElectronFiles(string installDir)
        {
            if (!Directory.Exists(installDir)) return;
            string[] legacyFiles = new string[]
            {
                "chrome_100_percent.pak",
                "chrome_200_percent.pak",
                "d3dcompiler_47.dll",
                "ffmpeg.dll",
                "icudtl.dat",
                "libEGL.dll",
                "libGLESv2.dll",
                "LICENSE.electron.txt",
                "LICENSES.chromium.html",
                "resources.pak",
                "snapshot_blob.bin",
                "v8_context_snapshot.bin",
                "vk_swiftshader.dll",
                "vk_swiftshader_icd.json",
                "vulkan-1.dll"
            };
            foreach (string f in legacyFiles)
            {
                string fp = Path.Combine(installDir, f);
                if (File.Exists(fp))
                {
                    try { File.Delete(fp); } catch { }
                }
            }
            string[] legacyDirs = new string[] { "locales", "resources" };
            foreach (string d in legacyDirs)
            {
                string dp = Path.Combine(installDir, d);
                if (Directory.Exists(dp))
                {
                    try { Directory.Delete(dp, true); } catch { }
                }
            }
        }

        private static void ExtractResourceToFile(string resourceName, string targetPath)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream(resourceName))
            {
                if (s == null)
                    throw new InvalidOperationException("Recurso embutido não encontrado: " + resourceName);
                using (FileStream fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
                {
                    s.CopyTo(fs);
                }
            }
        }

        public static void CreateShortcut(string shortcutPath, string targetExe, string workDir, string iconPath, string description)
        {
            try
            {
                string dir = Path.GetDirectoryName(shortcutPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var link = (IShellLinkW)new ShellLinkCoClass();
                link.SetPath(targetExe);
                link.SetWorkingDirectory(workDir);
                link.SetDescription(description);
                if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                {
                    link.SetIconLocation(iconPath, 0);
                }

                var persist = (IPersistFile)link;
                persist.Save(shortcutPath, false);
            }
            catch
            {
                // Ignore shortcut creation errors in restricted environments
            }
        }

        public static string PerformInstallation(
            string installDir,
            bool createDesktopShortcut,
            bool launchAtWindowsStartup,
            bool launchNow)
        {
            StopExistingDrawerProcesses();

            Directory.CreateDirectory(installDir);
            CleanupLegacyElectronFiles(installDir);

            string targetExe = Path.Combine(installDir, "Drawer.exe");
            string targetIcon = Path.Combine(installDir, "icon.ico");

            ExtractResourceToFile("Drawer.exe", targetExe);
            ExtractResourceToFile("icon.ico", targetIcon);

            // Start Menu shortcut
            string startMenuShortcut = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                "Drawer.lnk");
            CreateShortcut(startMenuShortcut, targetExe, installDir, targetIcon, "Drawer 2.0 — Organizador de Gavetas Nativas para Windows");

            // Desktop shortcut
            if (createDesktopShortcut)
            {
                string desktopShortcut = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "Drawer.lnk");
                CreateShortcut(desktopShortcut, targetExe, installDir, targetIcon, "Drawer 2.0 — Organizador de Gavetas Nativas para Windows");
            }

            // Windows Startup Registry
            try
            {
                using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (runKey != null)
                    {
                        if (launchAtWindowsStartup)
                        {
                            runKey.SetValue("Drawer", string.Format("\"{0}\" --autostart", targetExe), RegistryValueKind.String);
                        }
                        else if (runKey.GetValue("Drawer") != null)
                        {
                            runKey.DeleteValue("Drawer", false);
                        }
                    }
                }
            }
            catch { }

            // Also sync launchAtStartup in %APPDATA%\Drawer\drawers-state.json if it exists
            try
            {
                string stateFile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Drawer",
                    "drawers-state.json");
                if (File.Exists(stateFile))
                {
                    string content = File.ReadAllText(stateFile, Encoding.UTF8);
                    string targetBool = launchAtWindowsStartup ? "true" : "false";
                    content = System.Text.RegularExpressions.Regex.Replace(
                        content,
                        "\"launchAtStartup\"\\s*:\\s*(true|false)",
                        "\"launchAtStartup\": " + targetBool);
                    File.WriteAllText(stateFile, content, Encoding.UTF8);
                }
            }
            catch { }

            if (launchNow)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = targetExe,
                        WorkingDirectory = installDir,
                        UseShellExecute = true
                    });
                }
                catch { }
            }

            return targetExe;
        }
    }

    public class InstallerWindow : Window
    {
        public InstallerWindow()
        {
            Title = "Instalador — Drawer 2.0 (Pods Nativos & Bordas Magnéticas)";
            Width = 500;
            Height = 360;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            Foreground = Brushes.White;

            var stack = new StackPanel { Margin = new Thickness(24) };

            stack.Children.Add(new TextBlock
            {
                Text = "Drawer 2.0 — Edição 100% Nativa Windows",
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });

            stack.Children.Add(new TextBlock
            {
                Text = "Pods Nativos Independentes • Abas Magnéticas de Borda • Menu de Contexto Real do Windows • Catálogo Rápido de Apps",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(147, 197, 253)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 18)
            });

            var dirLabel = new TextBlock
            {
                Text = "Pasta de instalação:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                Margin = new Thickness(0, 0, 0, 4)
            };
            stack.Children.Add(dirLabel);

            var dirBox = new TextBox
            {
                Text = InstallerEngine.GetDefaultInstallDir(),
                Height = 28,
                Padding = new Thickness(6, 4, 6, 4),
                Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                Margin = new Thickness(0, 0, 0, 14)
            };
            stack.Children.Add(dirBox);

            var startupCheck = new CheckBox
            {
                Content = "Inicializar automaticamente junto com o Windows (Recomendado)",
                IsChecked = true,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(startupCheck);

            var desktopCheck = new CheckBox
            {
                Content = "Criar atalho na Área de Trabalho e Menu Iniciar",
                IsChecked = true,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(desktopCheck);

            var launchCheck = new CheckBox
            {
                Content = "Iniciar o Drawer 2.0 ao concluir a instalação",
                IsChecked = true,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 20)
            };
            stack.Children.Add(launchCheck);

            var installBtn = new Button
            {
                Content = "Instalar Drawer 2.0 Agora",
                Height = 38,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                Foreground = Brushes.White
            };

            installBtn.Click += delegate
            {
                try
                {
                    installBtn.IsEnabled = false;
                    installBtn.Content = "Instalando...";
                    InstallerEngine.PerformInstallation(
                        dirBox.Text.Trim(),
                        desktopCheck.IsChecked == true,
                        startupCheck.IsChecked == true,
                        launchCheck.IsChecked == true);
                    MessageBox.Show(
                        "Drawer 2.0 instalado com sucesso!",
                        "Drawer 2.0",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    Close();
                }
                catch (Exception ex)
                {
                    installBtn.IsEnabled = true;
                    installBtn.Content = "Instalar Drawer 2.0 Agora";
                    MessageBox.Show(
                        "Erro ao instalar: " + ex.Message,
                        "Drawer 2.0",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            };

            stack.Children.Add(installBtn);
            Content = stack;
        }
    }

    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            bool silent = false;
            bool launchNow = true;
            bool startup = true;

            if (args != null)
            {
                foreach (string a in args)
                {
                    if (a == "--silent" || a == "/S") silent = true;
                    if (a == "--no-launch") launchNow = false;
                    if (a == "--no-startup") startup = false;
                }
            }

            if (silent)
            {
                try
                {
                    InstallerEngine.PerformInstallation(
                        InstallerEngine.GetDefaultInstallDir(),
                        true,
                        startup,
                        launchNow);
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 1;
                }
            }

            var app = new Application();
            app.Run(new InstallerWindow());
            return 0;
        }
    }
}
