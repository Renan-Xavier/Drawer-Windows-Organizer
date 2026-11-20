using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DrawerNative
{
    public class DrawerPodWindow : Window
    {
        private readonly IDrawerAppController _controller;
        private DrawerModel _drawer;

        private bool _isOpen;
        private bool _isPinned;
        private bool _isContextMenuOpen;
        private bool _isDraggingPod;
        private Point _podDragStartScreen;
        private int _podStartDrawerX;
        private int _podStartDrawerY;

        private readonly DispatcherTimer _hoverCloseTimer;
        private readonly Canvas _rootCanvas;
        private readonly Border _edgeTabBorder;
        private readonly Border _handleContainer;
        private readonly Border _handleVisual;
        private readonly Grid _previewGrid;
        private readonly Border _pinBadge;
        private readonly Border _labelBadge;
        private readonly TextBlock _labelText;
        private readonly TextBox _inlineRenameBox;
        private readonly Border _panelBorder;
        private readonly Canvas _itemsCanvas;
        private readonly ScaleTransform _panelScale;

        public string DrawerId
        {
            get { return _drawer.id; }
        }

        public bool IsDrawerOpen
        {
            get { return _isOpen || _isPinned; }
        }

        public bool IsPinned
        {
            get { return _isPinned; }
        }

        public bool ShouldBeTopmost
        {
            get
            {
                return _controller.IsPeekModeActive ||
                       (_drawer.dockedEdge != null && _drawer.dockedEdge != "none");
            }
        }

        public bool IsBottomLayerLocked { get; private set; }

        public bool UsesWin32TrackPopupMenu
        {
            get { return true; }
        }

        public DrawerPodWindow(IDrawerAppController controller, DrawerModel drawer)
        {
            _controller = controller;
            _drawer = drawer;

            Title = "";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = false;
            AllowDrop = true;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            _hoverCloseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1250)
            };
            _hoverCloseTimer.Tick += OnHoverCloseTimerTick;

            _rootCanvas = new Canvas
            {
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                AllowDrop = true,
                ClipToBounds = false
            };

            // 1. Magnetic Edge Tab (shown when docked to left/right/top screen edge and closed)
            _edgeTabBorder = new Border
            {
                CornerRadius = new CornerRadius(0, 12, 12, 0),
                Cursor = Cursors.Hand,
                AllowDrop = true,
                Visibility = Visibility.Collapsed
            };

            // 2. Drawer Handle (Discord-style rounded square with 2x2 icon preview)
            _handleContainer = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                AllowDrop = true,
                Cursor = Cursors.Hand
            };

            _handleVisual = new Border
            {
                BorderThickness = new Thickness(1.5),
                AllowDrop = true,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 16,
                    ShadowDepth = 4,
                    Opacity = 0.48
                }
            };

            var handleInnerGrid = new Grid();
            _previewGrid = new Grid
            {
                Margin = new Thickness(7)
            };
            _previewGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _previewGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _pinBadge = new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(5),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 5, 5, 0),
                Background = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                BorderThickness = new Thickness(1.5),
                Visibility = Visibility.Collapsed
            };

            handleInnerGrid.Children.Add(_previewGrid);
            handleInnerGrid.Children.Add(_pinBadge);
            _handleVisual.Child = handleInnerGrid;
            _handleContainer.Child = _handleVisual;

            // 3. Label below handle + inline rename TextBox
            _labelText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.None,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            _labelBadge = new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 2, 8, 2),
                Background = new SolidColorBrush(Color.FromArgb(205, 9, 14, 26)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(85, 148, 163, 184)),
                BorderThickness = new Thickness(1),
                Child = _labelText,
                Cursor = Cursors.Hand
            };

            _inlineRenameBox = new TextBox
            {
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(96, 165, 250)),
                BorderThickness = new Thickness(1.5),
                Padding = new Thickness(4, 1, 4, 1),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            _inlineRenameBox.KeyDown += OnInlineRenameKeyDown;
            _inlineRenameBox.LostFocus += OnInlineRenameLostFocus;

            // 4. Expandable Geometric Icon Panel
            _panelScale = new ScaleTransform(1.0, 1.0);
            _panelBorder = new Border
            {
                BorderThickness = new Thickness(1.5),
                AllowDrop = true,
                RenderTransform = _panelScale,
                RenderTransformOrigin = new Point(0, 0),
                Visibility = Visibility.Collapsed,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 20,
                    ShadowDepth = 6,
                    Opacity = 0.52
                }
            };

            _itemsCanvas = new Canvas
            {
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                AllowDrop = true
            };
            _panelBorder.Child = _itemsCanvas;

            _rootCanvas.Children.Add(_panelBorder);
            _rootCanvas.Children.Add(_handleContainer);
            _rootCanvas.Children.Add(_labelBadge);
            _rootCanvas.Children.Add(_inlineRenameBox);
            _rootCanvas.Children.Add(_edgeTabBorder);

            Content = _rootCanvas;

            // Event Wiring
            SourceInitialized += OnWindowSourceInitialized;
            MouseEnter += OnPodMouseEnter;
            MouseLeave += OnPodMouseLeave;

            _edgeTabBorder.MouseEnter += OnPodMouseEnter;
            _edgeTabBorder.MouseLeftButtonDown += OnEdgeTabClicked;

            _handleContainer.MouseLeftButtonDown += OnHandleMouseLeftButtonDown;
            _handleContainer.MouseMove += OnHandleMouseMove;
            _handleContainer.MouseLeftButtonUp += OnHandleMouseLeftButtonUp;
            _handleContainer.MouseRightButtonUp += OnHandleMouseRightButtonUp;

            _labelBadge.MouseLeftButtonDown += OnLabelMouseLeftButtonDown;

            DragEnter += OnPodDragOver;
            DragOver += OnPodDragOver;
            Drop += OnPodDrop;
        }

        private void OnWindowSourceInitialized(object sender, EventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            Win32ShellService.ConfigurePodToolWindowStyle(hwnd);
            Win32ShellService.EnableNativeFileDropOnWindow(hwnd);
            HwndSource src = HwndSource.FromHwnd(hwnd);
            if (src != null)
            {
                src.AddHook(PodWndProc);
                IsBottomLayerLocked = true;
            }
            RefreshLayering();
        }

        private IntPtr PodWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32ShellService.WM_DROPFILES)
            {
                handled = true;
                string[] droppedFiles = Win32ShellService.ExtractDroppedFilesFromHDrop(wParam);
                if (droppedFiles != null && droppedFiles.Length > 0)
                {
                    _hoverCloseTimer.Stop();
                    _isOpen = true;
                    _controller.AddFilesToDrawer(_drawer.id, droppedFiles);
                }
                return IntPtr.Zero;
            }
            if (msg == Win32ShellService.WM_MOUSEACTIVATE)
            {
                if (!ShouldBeTopmost && _inlineRenameBox.Visibility != Visibility.Visible)
                {
                    handled = true;
                    return new IntPtr(Win32ShellService.MA_NOACTIVATE);
                }
            }
            else if (msg == Win32ShellService.WM_WINDOWPOSCHANGING)
            {
                if (!ShouldBeTopmost && !_isContextMenuOpen && lParam != IntPtr.Zero)
                {
                    try
                    {
                        var wp = (Win32ShellService.WINDOWPOS)Marshal.PtrToStructure(
                            lParam,
                            typeof(Win32ShellService.WINDOWPOS));
                        wp.hwndInsertAfter = Win32ShellService.HWND_BOTTOM;
                        wp.flags |= Win32ShellService.SWP_NOACTIVATE;
                        Marshal.StructureToPtr(wp, lParam, false);
                    }
                    catch { }
                }
            }
            return IntPtr.Zero;
        }

        public void RefreshLayering()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            bool shouldBeTopmost = ShouldBeTopmost;
            Topmost = shouldBeTopmost;
            Win32ShellService.ApplyPodLayering(hwnd, shouldBeTopmost);
            if (!shouldBeTopmost)
            {
                IsBottomLayerLocked = true;
            }
        }

        public void UpdateDrawerModel(DrawerModel updated)
        {
            if (updated == null) return;
            _drawer = updated;
            RenderPod();
            RefreshLayering();
        }

        private void OnPodMouseEnter(object sender, MouseEventArgs e)
        {
            _hoverCloseTimer.Stop();
            if (!_isOpen)
            {
                _isOpen = true;
                RenderPod();
                AnimatePanelOpen();
            }
        }

        private void OnPodMouseLeave(object sender, MouseEventArgs e)
        {
            if (_isPinned || _isContextMenuOpen || _isDraggingPod) return;
            _hoverCloseTimer.Stop();
            _hoverCloseTimer.Start();
        }

        private void OnHoverCloseTimerTick(object sender, EventArgs e)
        {
            _hoverCloseTimer.Stop();
            if (_isPinned || _isContextMenuOpen || _isDraggingPod || IsMouseOver) return;
            _isOpen = false;
            RenderPod();
        }

        private void OnEdgeTabClicked(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            _hoverCloseTimer.Stop();
            _isOpen = true;
            _isPinned = true;
            RenderPod();
            AnimatePanelOpen();
        }

        private void AnimatePanelOpen()
        {
            if (_panelBorder.Visibility != Visibility.Visible) return;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = new Duration(TimeSpan.FromMilliseconds(260));

            var animX = new DoubleAnimation(0.86, 1.0, dur) { EasingFunction = ease };
            var animY = new DoubleAnimation(0.86, 1.0, dur) { EasingFunction = ease };
            var animOp = new DoubleAnimation(0.25, 1.0, dur) { EasingFunction = ease };

            _panelScale.BeginAnimation(ScaleTransform.ScaleXProperty, animX);
            _panelScale.BeginAnimation(ScaleTransform.ScaleYProperty, animY);
            _panelBorder.BeginAnimation(UIElement.OpacityProperty, animOp);
        }

        private void OnHandleMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            _hoverCloseTimer.Stop();
            _isDraggingPod = false;

            Win32ShellService.POINT pt;
            Win32ShellService.GetCursorPos(out pt);
            _podDragStartScreen = new Point(pt.X, pt.Y);
            _podStartDrawerX = _drawer.x;
            _podStartDrawerY = _drawer.y;

            _handleContainer.CaptureMouse();
        }

        private void OnHandleMouseMove(object sender, MouseEventArgs e)
        {
            if (!_handleContainer.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;

            Win32ShellService.POINT pt;
            Win32ShellService.GetCursorPos(out pt);
            double dx = pt.X - _podDragStartScreen.X;
            double dy = pt.Y - _podDragStartScreen.Y;

            if (!_isDraggingPod && Math.Sqrt(dx * dx + dy * dy) > 5)
            {
                _isDraggingPod = true;
            }

            if (_isDraggingPod)
            {
                _drawer.x = (int)Math.Round(_podStartDrawerX + dx);
                _drawer.y = (int)Math.Round(_podStartDrawerY + dy);
                _drawer.dockedEdge = "none";
                RenderPod();
            }
        }

        private void OnHandleMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_handleContainer.IsMouseCaptured)
            {
                _handleContainer.ReleaseMouseCapture();
            }

            if (_isDraggingPod)
            {
                _isDraggingPod = false;
                _controller.MoveDrawer(_drawer.id, _drawer.x, _drawer.y);
            }
            else
            {
                // Single click -> toggle Pin
                if (_isPinned)
                {
                    _isPinned = false;
                    _isOpen = false;
                }
                else
                {
                    _isPinned = true;
                    _isOpen = true;
                }
                RenderPod();
                if (_isOpen) AnimatePanelOpen();
            }
        }

        private void OnLabelMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 2)
            {
                e.Handled = true;
                BeginInlineRename();
            }
        }

        public void BeginInlineRename()
        {
            _isPinned = true;
            _isOpen = true;
            RenderPod();

            _labelBadge.Visibility = Visibility.Collapsed;
            _inlineRenameBox.Text = _drawer.name;
            _inlineRenameBox.Visibility = Visibility.Visible;
            Activate();
            _inlineRenameBox.Focus();
            _inlineRenameBox.SelectAll();
        }

        private void CommitInlineRename()
        {
            if (_inlineRenameBox.Visibility != Visibility.Visible) return;
            string nextName = _inlineRenameBox.Text;
            _inlineRenameBox.Visibility = Visibility.Collapsed;
            _labelBadge.Visibility = Visibility.Visible;
            if (!string.IsNullOrWhiteSpace(nextName))
            {
                _controller.RenameDrawer(_drawer.id, nextName.Trim());
            }
        }

        private void OnInlineRenameKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                CommitInlineRename();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                _inlineRenameBox.Visibility = Visibility.Collapsed;
                _labelBadge.Visibility = Visibility.Visible;
            }
        }

        private void OnInlineRenameLostFocus(object sender, RoutedEventArgs e)
        {
            CommitInlineRename();
        }

        private void OnHandleMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            _isContextMenuOpen = true;
            _hoverCloseTimer.Stop();

            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                Win32ShellService.POINT pt;
                Win32ShellService.GetCursorPos(out pt);

                int cmd = Win32ShellService.ShowNativePodHandleContextMenu(
                    hwnd,
                    pt.X,
                    pt.Y,
                    _isPinned,
                    _drawer.dockedEdge,
                    _drawer.preferredDirection);

                switch (cmd)
                {
                    case 1:
                        _isPinned = !_isPinned;
                        _isOpen = _isPinned;
                        RenderPod();
                        break;
                    case 2:
                        _controller.OpenCatalogForDrawer(_drawer.id);
                        break;
                    case 3:
                        _controller.PickFilesForDrawer(_drawer.id);
                        break;
                    case 4:
                        BeginInlineRename();
                        break;
                    case 5:
                        _controller.CreateNewDrawer();
                        break;
                    case 6:
                        _controller.OpenSettingsWindow();
                        break;
                    case 7:
                        _controller.DeleteDrawer(_drawer.id);
                        break;
                    case 20:
                        _controller.SetDrawerDockedEdge(_drawer.id, "none");
                        break;
                    case 21:
                        _controller.SetDrawerDockedEdge(_drawer.id, "left");
                        break;
                    case 22:
                        _controller.SetDrawerDockedEdge(_drawer.id, "right");
                        break;
                    case 23:
                        _controller.SetDrawerDockedEdge(_drawer.id, "top");
                        break;
                    case 30:
                        _controller.SetDrawerDirection(_drawer.id, "auto");
                        break;
                    case 31:
                        _controller.SetDrawerDirection(_drawer.id, "right");
                        break;
                    case 32:
                        _controller.SetDrawerDirection(_drawer.id, "down");
                        break;
                    case 33:
                        _controller.SetDrawerDirection(_drawer.id, "left");
                        break;
                }
            }
            finally
            {
                _isContextMenuOpen = false;
                RefreshLayering();
                if (!_isPinned && !IsMouseOver)
                {
                    _hoverCloseTimer.Stop();
                    _hoverCloseTimer.Start();
                }
            }
        }

        private void OnPodDragOver(object sender, DragEventArgs e)
        {
            _hoverCloseTimer.Stop();
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent("DrawerItemDrag"))
            {
                if ((e.AllowedEffects & DragDropEffects.Copy) != 0)
                    e.Effects = DragDropEffects.Copy;
                else if ((e.AllowedEffects & DragDropEffects.Move) != 0)
                    e.Effects = DragDropEffects.Move;
                else
                    e.Effects = DragDropEffects.Link;

                if (!_isOpen)
                {
                    _isOpen = true;
                    RenderPod();
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void OnPodDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            _hoverCloseTimer.Stop();
            if (e.Data.GetDataPresent("DrawerItemDrag"))
            {
                string payload = e.Data.GetData("DrawerItemDrag") as string;
                if (!string.IsNullOrEmpty(payload))
                {
                    string[] parts = payload.Split('|');
                    if (parts.Length == 2 && parts[0] != _drawer.id)
                    {
                        _controller.MoveItemBetweenDrawers(parts[0], parts[1], _drawer.id);
                    }
                }
                return;
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    _isOpen = true;
                    _controller.AddFilesToDrawer(_drawer.id, files);
                }
            }
        }

        private void ApplyThemeBrushes(ScaleMetrics metrics, bool isTransparentMode)
        {
            _handleVisual.CornerRadius = new CornerRadius(metrics.BorderRadius);
            _panelBorder.CornerRadius = new CornerRadius(metrics.BorderRadius);

            if (isTransparentMode)
            {
                _handleVisual.Background = new SolidColorBrush(Color.FromArgb(14, 15, 23, 42));
                _handleVisual.BorderBrush = new SolidColorBrush(Color.FromArgb(175, 148, 163, 184));
                _panelBorder.Background = new SolidColorBrush(Color.FromArgb(18, 15, 23, 42));
                _panelBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(175, 148, 163, 184));
            }
            else
            {
                var handleGrad = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1)
                };
                handleGrad.GradientStops.Add(new GradientStop(Color.FromArgb(238, 37, 99, 235), 0.0));
                handleGrad.GradientStops.Add(new GradientStop(Color.FromArgb(238, 23, 37, 84), 1.0));
                _handleVisual.Background = handleGrad;
                _handleVisual.BorderBrush = new SolidColorBrush(Color.FromArgb(190, 96, 165, 250));

                var panelGrad = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1)
                };
                panelGrad.GradientStops.Add(new GradientStop(Color.FromArgb(242, 23, 37, 84), 0.0));
                panelGrad.GradientStops.Add(new GradientStop(Color.FromArgb(245, 15, 23, 42), 1.0));
                _panelBorder.Background = panelGrad;
                _panelBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(165, 96, 165, 250));
            }
        }

        public void RenderPod()
        {
            string scale = _controller.GetEffectiveScale();
            bool isTransparent = _controller.GetBackgroundMode() == "transparent";
            ScaleMetrics metrics = DrawerGeometryEngine.GetScaleMetrics(scale);

            int screenW = (int)SystemParameters.WorkArea.Width;
            int screenH = (int)SystemParameters.WorkArea.Height;

            string dockedEdge = string.IsNullOrEmpty(_drawer.dockedEdge) ? "none" : _drawer.dockedEdge;
            bool isEdgeDocked = (dockedEdge == "left" || dockedEdge == "right" || dockedEdge == "top");
            bool showOpenPanel = (_isOpen || _isPinned);

            ApplyThemeBrushes(metrics, isTransparent);

            // Collapsed Magnetic Edge Tab Mode (when docked to screen edge and closed)
            if (isEdgeDocked && !showOpenPanel)
            {
                RenderCollapsedEdgeTab(metrics, dockedEdge, screenW, screenH);
                return;
            }

            _edgeTabBorder.Visibility = Visibility.Collapsed;
            _handleContainer.Visibility = Visibility.Visible;
            if (_inlineRenameBox.Visibility != Visibility.Visible)
                _labelBadge.Visibility = Visibility.Visible;

            // Pin / Edge indicator badge
            if (_isPinned)
            {
                _pinBadge.Background = new SolidColorBrush(Color.FromRgb(56, 189, 248));
                _pinBadge.Visibility = Visibility.Visible;
            }
            else if (isEdgeDocked)
            {
                _pinBadge.Background = new SolidColorBrush(Color.FromRgb(167, 139, 250));
                _pinBadge.Visibility = Visibility.Visible;
            }
            else
            {
                _pinBadge.Visibility = Visibility.Collapsed;
            }

            RenderPreview2x2(metrics);
            _labelText.Text = _drawer.name;

            int itemCount = (_drawer.items != null) ? _drawer.items.Count : 0;
            string direction = DrawerGeometryEngine.ComputeOpenDirection(
                _drawer.x,
                _drawer.y,
                itemCount,
                scale,
                screenW,
                screenH,
                _drawer.preferredDirection,
                dockedEdge);

            GridDimensions grid = DrawerGeometryEngine.ComputeDrawerGrid(itemCount, direction, scale);

            int labelH = DrawerGeometryEngine.LABEL_HEIGHT_PX;
            PodPlacementResult placement = DrawerGeometryEngine.ComputePodPanelPlacement(
                _drawer.x,
                _drawer.y,
                metrics.DrawerSize,
                _drawer.name,
                direction,
                grid,
                screenW);

            int handleScreenX = placement.HandleX;
            int handleScreenY = placement.HandleY;
            int handleW = placement.HandleSize;
            int handleH = placement.HandleSize;

            int labelW = placement.LabelWidth;
            int labelScreenX = placement.LabelX;

            int baseScreenX = Math.Min(handleScreenX, labelScreenX);
            int baseScreenY = handleScreenY;
            int baseRight = Math.Max(handleScreenX + handleW, labelScreenX + labelW);
            int baseW = baseRight - baseScreenX;
            int baseH = handleH + labelH;

            int panelScreenX = placement.PanelX;
            int panelScreenY = placement.PanelY;

            if (direction == "right")
            {
                _panelBorder.RenderTransformOrigin = new Point(0.0, 0.2);
            }
            else if (direction == "left")
            {
                _panelBorder.RenderTransformOrigin = new Point(1.0, 0.2);
            }
            else
            {
                _panelBorder.RenderTransformOrigin = new Point(0.2, 0.0);
            }

            int winLeft = baseScreenX;
            int winTop = baseScreenY;
            int winRight = baseScreenX + baseW;
            int winBottom = baseScreenY + baseH;

            if (showOpenPanel)
            {
                winLeft = Math.Min(winLeft, panelScreenX);
                winTop = Math.Min(winTop, panelScreenY);
                winRight = Math.Max(winRight, panelScreenX + grid.Width);
                winBottom = Math.Max(winBottom, panelScreenY + grid.Height);
            }

            Left = winLeft;
            Top = winTop;
            Width = Math.Max(40, winRight - winLeft);
            Height = Math.Max(40, winBottom - winTop);

            double localHandleX = handleScreenX - winLeft;
            double localHandleY = handleScreenY - winTop;
            double localLabelX = labelScreenX - winLeft;

            _handleContainer.Width = handleW;
            _handleContainer.Height = handleH;
            Canvas.SetLeft(_handleContainer, localHandleX);
            Canvas.SetTop(_handleContainer, localHandleY);

            _labelBadge.MaxWidth = labelW;
            _labelBadge.MinWidth = Math.Min(labelW, 38);
            _labelBadge.Width = labelW;
            Canvas.SetLeft(_labelBadge, localLabelX);
            Canvas.SetTop(_labelBadge, localHandleY + handleH + 3);

            _inlineRenameBox.Width = labelW;
            Canvas.SetLeft(_inlineRenameBox, localLabelX);
            Canvas.SetTop(_inlineRenameBox, localHandleY + handleH + 2);

            if (showOpenPanel)
            {
                _panelBorder.Visibility = Visibility.Visible;
                _panelBorder.Width = grid.Width;
                _panelBorder.Height = grid.Height;
                Canvas.SetLeft(_panelBorder, panelScreenX - winLeft);
                Canvas.SetTop(_panelBorder, panelScreenY - winTop);
                RenderOpenPanelItems(grid, metrics);
            }
            else
            {
                _panelBorder.Visibility = Visibility.Collapsed;
            }

            RefreshLayering();
        }

        private void RenderCollapsedEdgeTab(ScaleMetrics metrics, string dockedEdge, int screenW, int screenH)
        {
            _handleContainer.Visibility = Visibility.Collapsed;
            _labelBadge.Visibility = Visibility.Collapsed;
            _inlineRenameBox.Visibility = Visibility.Collapsed;
            _panelBorder.Visibility = Visibility.Collapsed;
            _edgeTabBorder.Visibility = Visibility.Visible;

            var tabGrad = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            tabGrad.GradientStops.Add(new GradientStop(Color.FromArgb(242, 37, 99, 235), 0.0));
            tabGrad.GradientStops.Add(new GradientStop(Color.FromArgb(245, 23, 37, 84), 1.0));
            _edgeTabBorder.Background = tabGrad;
            _edgeTabBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(230, 96, 165, 250));
            _edgeTabBorder.BorderThickness = new Thickness(1.5);

            var rootStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4)
            };

            // Mini 2x2 Discord preview inside the magnetic edge tab
            var miniGrid = new Grid
            {
                Width = 30,
                Height = 30,
                Margin = new Thickness(0, 1, 0, 3)
            };
            miniGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            miniGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            miniGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            miniGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            List<DrawerItem> items = _drawer.items ?? new List<DrawerItem>();
            for (int i = 0; i < 4; i++)
            {
                var cell = new Border
                {
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(1),
                    Background = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255))
                };
                if (i < items.Count && !string.IsNullOrEmpty(items[i].iconDataUrl))
                {
                    BitmapSource bmp = Win32ShellService.DataUrlToBitmapSource(items[i].iconDataUrl);
                    if (bmp != null)
                    {
                        var img = new Image
                        {
                            Source = bmp,
                            Width = 11,
                            Height = 11,
                            Stretch = Stretch.Uniform
                        };
                        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                        cell.Child = img;
                    }
                }
                Grid.SetRow(cell, i / 2);
                Grid.SetColumn(cell, i % 2);
                miniGrid.Children.Add(cell);
            }
            rootStack.Children.Add(miniGrid);

            var tabLabel = new TextBlock
            {
                Text = _drawer.name,
                Foreground = Brushes.White,
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                MaxWidth = 42,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            rootStack.Children.Add(tabLabel);

            _edgeTabBorder.Child = rootStack;
            _edgeTabBorder.ToolTip = string.Format("{0} ({1} itens) — Aba Magnética (Passe o mouse para abrir)",
                _drawer.name, items.Count);

            if (dockedEdge == "left")
            {
                int tabW = 48;
                int tabH = 60;
                _edgeTabBorder.CornerRadius = new CornerRadius(0, 14, 14, 0);
                _edgeTabBorder.Width = tabW;
                _edgeTabBorder.Height = tabH;
                Left = 0;
                Top = _drawer.y;
                Width = tabW;
                Height = tabH;
                Canvas.SetLeft(_edgeTabBorder, 0);
                Canvas.SetTop(_edgeTabBorder, 0);
            }
            else if (dockedEdge == "right")
            {
                int tabW = 48;
                int tabH = 60;
                _edgeTabBorder.CornerRadius = new CornerRadius(14, 0, 0, 14);
                _edgeTabBorder.Width = tabW;
                _edgeTabBorder.Height = tabH;
                Left = Math.Max(0, screenW - tabW);
                Top = _drawer.y;
                Width = tabW;
                Height = tabH;
                Canvas.SetLeft(_edgeTabBorder, 0);
                Canvas.SetTop(_edgeTabBorder, 0);
            }
            else
            {
                // top
                int tabW = 64;
                int tabH = 54;
                _edgeTabBorder.CornerRadius = new CornerRadius(0, 0, 14, 14);
                _edgeTabBorder.Width = tabW;
                _edgeTabBorder.Height = tabH;
                Left = _drawer.x;
                Top = 0;
                Width = tabW;
                Height = tabH;
                Canvas.SetLeft(_edgeTabBorder, 0);
                Canvas.SetTop(_edgeTabBorder, 0);
            }
        }

        private void RenderPreview2x2(ScaleMetrics metrics)
        {
            _previewGrid.Children.Clear();
            List<DrawerItem> items = _drawer.items ?? new List<DrawerItem>();

            for (int i = 0; i < 4; i++)
            {
                int row = i / 2;
                int col = i % 2;

                var slot = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    Margin = new Thickness(1.5),
                    Background = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255))
                };

                if (i < items.Count)
                {
                    DrawerItem item = items[i];
                    BitmapSource bmp = Win32ShellService.DataUrlToBitmapSource(item.iconDataUrl);
                    if (bmp != null)
                    {
                        var img = new Image
                        {
                            Source = bmp,
                            Width = metrics.PreviewIconSize,
                            Height = metrics.PreviewIconSize,
                            Stretch = Stretch.Uniform
                        };
                        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                        slot.Child = img;
                    }
                    else
                    {
                        slot.Child = new TextBlock
                        {
                            Text = !string.IsNullOrEmpty(item.name) ? item.name.Substring(0, 1).ToUpperInvariant() : "?",
                            Foreground = Brushes.White,
                            FontSize = 10,
                            FontWeight = FontWeights.Bold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                    }
                }

                Grid.SetRow(slot, row);
                Grid.SetColumn(slot, col);
                _previewGrid.Children.Add(slot);
            }
        }

        private void RenderOpenPanelItems(GridDimensions grid, ScaleMetrics metrics)
        {
            _itemsCanvas.Children.Clear();
            _itemsCanvas.Width = grid.Width;
            _itemsCanvas.Height = grid.Height;

            List<DrawerItem> items = _drawer.items ?? new List<DrawerItem>();
            if (items.Count == 0)
            {
                var emptyBtn = new Border
                {
                    Width = metrics.CellWidth,
                    Height = metrics.CellHeight,
                    CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(Color.FromArgb(40, 96, 165, 250)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(120, 96, 165, 250)),
                    BorderThickness = new Thickness(1.5),
                    Cursor = Cursors.Hand,
                    ToolTip = "Clique para abrir o Catálogo Rápido de Apps ou arraste atalhos para cá"
                };

                var stack = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                stack.Children.Add(new TextBlock
                {
                    Text = "+",
                    Foreground = new SolidColorBrush(Color.FromRgb(147, 197, 253)),
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                stack.Children.Add(new TextBlock
                {
                    Text = "Adicionar",
                    Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                emptyBtn.Child = stack;

                emptyBtn.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    _controller.OpenCatalogForDrawer(_drawer.id);
                };

                Canvas.SetLeft(emptyBtn, metrics.Padding);
                Canvas.SetTop(emptyBtn, metrics.Padding);
                _itemsCanvas.Children.Add(emptyBtn);
                return;
            }

            for (int idx = 0; idx < items.Count; idx++)
            {
                DrawerItem item = items[idx];
                int col = idx % grid.Cols;
                int row = idx / grid.Cols;

                double cellX = metrics.Padding + col * (metrics.CellWidth + metrics.Gap);
                double cellY = metrics.Padding + row * (metrics.CellHeight + metrics.Gap);

                Border cell = CreateIconCell(item, metrics);
                Canvas.SetLeft(cell, cellX);
                Canvas.SetTop(cell, cellY);
                _itemsCanvas.Children.Add(cell);
            }
        }

        private Border CreateIconCell(DrawerItem item, ScaleMetrics metrics)
        {
            var cell = new Border
            {
                Width = metrics.CellWidth,
                Height = metrics.CellHeight,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(2, 255, 255, 255)),
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = string.Format("{0}\n• Clique esquerdo: Abrir\n• Botão direito: Menu Nativo do Windows\n• Arrastar para fora: Devolver ao Desktop", item.name)
            };

            cell.MouseEnter += delegate
            {
                cell.Background = new SolidColorBrush(Color.FromArgb(65, 96, 165, 250));
                cell.BorderBrush = new SolidColorBrush(Color.FromArgb(140, 147, 197, 253));
            };
            cell.MouseLeave += delegate
            {
                cell.Background = new SolidColorBrush(Color.FromArgb(2, 255, 255, 255));
                cell.BorderBrush = Brushes.Transparent;
            };

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 0, 0)
            };

            BitmapSource bmp = Win32ShellService.DataUrlToBitmapSource(item.iconDataUrl);
            if (bmp != null)
            {
                var img = new Image
                {
                    Source = bmp,
                    Width = metrics.IconSize,
                    Height = metrics.IconSize,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 0, 0, 2)
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                stack.Children.Add(img);
            }
            else
            {
                var fallback = new Border
                {
                    Width = metrics.IconSize,
                    Height = metrics.IconSize,
                    CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                    Margin = new Thickness(0, 0, 0, 2),
                    Child = new TextBlock
                    {
                        Text = !string.IsNullOrEmpty(item.name) ? item.name.Substring(0, 1).ToUpperInvariant() : "?",
                        Foreground = Brushes.White,
                        FontSize = metrics.IconSize * 0.42,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                stack.Children.Add(fallback);
            }

            var lbl = new TextBlock
            {
                Text = item.name,
                Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                FontSize = 10.5,
                FontWeight = FontWeights.Medium,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                LineHeight = 12,
                MaxWidth = metrics.CellWidth - 4,
                MaxHeight = 25,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(lbl);
            cell.Child = stack;

            Point dragStartScreen = new Point();
            bool isDown = false;
            bool isDraggingItem = false;
            DragGhostWindow ghostWin = null;

            cell.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                _hoverCloseTimer.Stop();
                isDown = true;
                isDraggingItem = false;
                Win32ShellService.POINT pt;
                Win32ShellService.GetCursorPos(out pt);
                dragStartScreen = new Point(pt.X, pt.Y);
                cell.CaptureMouse();
            };

            cell.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (!isDown || !cell.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
                Win32ShellService.POINT curPt;
                Win32ShellService.GetCursorPos(out curPt);
                double dx = curPt.X - dragStartScreen.X;
                double dy = curPt.Y - dragStartScreen.Y;

                if (!isDraggingItem && Math.Sqrt(dx * dx + dy * dy) > 6)
                {
                    isDraggingItem = true;
                    cell.Opacity = 0.35;
                    ghostWin = new DragGhostWindow(item, metrics.IconSize);
                    ghostWin.Show();
                }

                if (isDraggingItem && ghostWin != null)
                {
                    string hitDrawerId = _controller.FindDrawerPodAtScreenPoint(curPt.X, curPt.Y);
                    if (string.IsNullOrEmpty(hitDrawerId))
                    {
                        ghostWin.UpdatePositionAndHint(curPt.X, curPt.Y, "Soltar aqui na Área de Trabalho");
                    }
                    else if (hitDrawerId == _drawer.id)
                    {
                        ghostWin.UpdatePositionAndHint(curPt.X, curPt.Y, "Arraste para fora para mover ao Desktop");
                    }
                    else
                    {
                        ghostWin.UpdatePositionAndHint(curPt.X, curPt.Y, "Mover para outra Gaveta");
                    }
                }
            };

            cell.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                if (cell.IsMouseCaptured)
                {
                    cell.ReleaseMouseCapture();
                }
                cell.Opacity = 1.0;

                if (ghostWin != null)
                {
                    try { ghostWin.Close(); } catch { }
                    ghostWin = null;
                }

                if (!isDown) return;
                isDown = false;
                e.Handled = true;

                if (!isDraggingItem)
                {
                    _controller.LaunchItem(_drawer.id, item);
                    return;
                }

                isDraggingItem = false;
                Win32ShellService.POINT dropPt;
                Win32ShellService.GetCursorPos(out dropPt);

                string targetDrawerId = _controller.FindDrawerPodAtScreenPoint(dropPt.X, dropPt.Y);
                if (targetDrawerId == _drawer.id)
                {
                    return;
                }
                if (!string.IsNullOrEmpty(targetDrawerId) && targetDrawerId != _drawer.id)
                {
                    _controller.MoveItemBetweenDrawers(_drawer.id, item.id, targetDrawerId);
                    return;
                }

                // Dropped on the Windows Desktop at (dropPt.X, dropPt.Y)!
                _controller.RestoreItemToDesktopAtPosition(_drawer.id, item.id, dropPt.X, dropPt.Y);
            };

            // RIGHT CLICK -> Real Windows Shell Context Menu (IContextMenu)!
            cell.MouseRightButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                ShowNativeItemContextMenu(item);
            };

            return cell;
        }

        private void ShowNativeItemContextMenu(DrawerItem item)
        {
            _isContextMenuOpen = true;
            _hoverCloseTimer.Stop();

            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                Win32ShellService.POINT pt;
                Win32ShellService.GetCursorPos(out pt);

                string target = item.targetPath;
                if ((string.IsNullOrEmpty(target) || (!File.Exists(target) && !Directory.Exists(target))) &&
                    !string.IsNullOrEmpty(item.originalDesktopPath))
                {
                    target = item.originalDesktopPath;
                }

                bool shown = Win32ShellService.ShowNativeShellContextMenu(
                    hwnd,
                    target,
                    item.name,
                    pt.X,
                    pt.Y,
                    delegate { _controller.LaunchItem(_drawer.id, item); },
                    delegate { _controller.PromptRenameItem(_drawer.id, item); },
                    delegate { _controller.RestoreItemToDesktop(_drawer.id, item.id); });

                if (!shown)
                {
                    // Fallback WPF menu if target file was moved/deleted outside Drawer
                    var fallbackMenu = new ContextMenu();
                    var openItem = new MenuItem { Header = string.Format("Abrir \"{0}\"", item.name) };
                    openItem.Click += delegate { _controller.LaunchItem(_drawer.id, item); };
                    var restoreItem = new MenuItem { Header = "Remover / Devolver para a Área de Trabalho" };
                    restoreItem.Click += delegate { _controller.RestoreItemToDesktop(_drawer.id, item.id); };
                    fallbackMenu.Items.Add(openItem);
                    fallbackMenu.Items.Add(restoreItem);
                    fallbackMenu.IsOpen = true;
                }
            }
            finally
            {
                _isContextMenuOpen = false;
                if (!_isPinned && !IsMouseOver)
                {
                    _hoverCloseTimer.Stop();
                    _hoverCloseTimer.Start();
                }
            }
        }
    }

    /// <summary>
    /// Floating native icon preview window that follows the cursor in real time while dragging an item out of a Drawer.
    /// </summary>
    public class DragGhostWindow : Window
    {
        private readonly TextBlock _hintText;

        public DragGhostWindow(DrawerItem item, int iconSize)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Width = 150;
            Height = iconSize + 52;
            IsHitTestVisible = false;

            var rootStack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var card = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 5, 8, 5),
                Background = new SolidColorBrush(Color.FromArgb(225, 15, 23, 42)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(220, 96, 165, 250)),
                BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var cardStack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center
            };

            BitmapSource bmp = Win32ShellService.DataUrlToBitmapSource(item != null ? item.iconDataUrl : "");
            if (bmp != null)
            {
                var img = new Image
                {
                    Source = bmp,
                    Width = iconSize,
                    Height = iconSize,
                    Stretch = Stretch.Uniform
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                cardStack.Children.Add(img);
            }

            cardStack.Children.Add(new TextBlock
            {
                Text = item != null ? item.name : "Atalho",
                Foreground = Brushes.White,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                MaxWidth = 120,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0)
            });

            card.Child = cardStack;
            rootStack.Children.Add(card);

            _hintText = new TextBlock
            {
                Text = "Soltar aqui na Área de Trabalho",
                Foreground = new SolidColorBrush(Color.FromRgb(147, 197, 253)),
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var hintBadge = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 3, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(230, 9, 14, 26)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(140, 56, 189, 248)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = _hintText
            };
            rootStack.Children.Add(hintBadge);

            Content = rootStack;

            SourceInitialized += delegate
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                long ex = Win32ShellService.GetWindowLongPtr(hwnd, Win32ShellService.GWL_EXSTYLE).ToInt64() & 0xFFFFFFFFL;
                long desired = (ex | Win32ShellService.WS_EX_TOOLWINDOW | Win32ShellService.WS_EX_NOACTIVATE | Win32ShellService.WS_EX_TRANSPARENT) & 0xFFFFFFFFL;
                Win32ShellService.SetWindowLongPtr(hwnd, Win32ShellService.GWL_EXSTYLE, new IntPtr(desired));
            };
        }

        public void UpdatePositionAndHint(int screenX, int screenY, string hint)
        {
            Left = screenX - (Width / 2);
            Top = screenY - 22;
            if (_hintText != null)
            {
                _hintText.Text = hint;
            }
        }
    }
}
