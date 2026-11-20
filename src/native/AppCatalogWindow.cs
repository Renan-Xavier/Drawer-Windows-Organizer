using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DrawerNative
{
    public class AppCatalogWindow : Window
    {
        public const string AuthorSupportHeaderLine1 =
            "Caso esse programa tenha lhe ajudado em algo, pense na hipotese de me ajudar também financeiramente para continuar apoiando meus projetos, no pix abaixo, qualquer valor ja ajudaria.";
        public const string AuthorSupportHeaderLine2 =
            "Aceito transferencias Via PIX: +55 21 985692631";
        public const string AuthorSupportHeaderLine3 =
            "Em nome de: Renan Santa Rosa Xavier - Mercado pago";
        public const string AuthorSupportHeaderLine4 =
            "E também aceito contatos via Whatsapp para trabalho ou feedback de melhorias no projeto.";

        public const string AuthorOfficialWarningText =
            "AVISO: ESTE PROGRAMA FOI TOTALMENTE DESENVOLVIDO POR APENAS RENAN SANTA ROSA XAVIER, E  NÃO ME RESPONSABILIZO POR FORNECIMENTO DESTE PROGRAMA EM SITES DE TERCEIROS, E UM PROGRAMA DE CONDIGO ABERTO E QUALQUER PESSOA PODE TER ACESSO E MANIPULAR OS ARQUIVOS, NÃO BAIXE POR NENHUM LUGAR QUE NÃO SEJA PELO MEU GITHUB OFICIAL.";

        private readonly IDrawerAppController _controller;
        private string _selectedDrawerId;
        private List<CatalogEntry> _allCatalogEntries;
        private string _sourceFilter = "all"; // "all" | "desktop" | "startmenu"

        private ComboBox _targetDrawerCombo;
        private TextBox _searchBox;
        private StackPanel _catalogListPanel;
        private StackPanel _drawersListPanel;
        private ComboBox _scaleCombo;
        private ComboBox _themeCombo;
        private CheckBox _startupCheck;
        private CheckBox _vaultCheck;
        private TextBlock _statusBanner;

        public AppCatalogWindow(IDrawerAppController controller, string initialDrawerId)
        {
            _controller = controller;
            _selectedDrawerId = initialDrawerId;

            Title = "Drawer 2.0 — Catálogo Rápido de Apps & Configurações (Por Renan Santa Rosa Xavier)";
            Width = 860;
            Height = 760;
            MinWidth = 760;
            MinHeight = 650;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252));
            UseLayoutRounding = true;

            BuildLayout();
            LoadCatalogData();
            RefreshUI();
        }

        public void SelectDrawer(string drawerId)
        {
            if (!string.IsNullOrEmpty(drawerId))
            {
                _selectedDrawerId = drawerId;
                RefreshUI();
            }
        }

        private void BuildLayout()
        {
            var rootGrid = new Grid { Margin = new Thickness(16) };
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header with PIX & WhatsApp Author Support Message
            var headerBorder = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 12),
                Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, 96, 165, 250)),
                BorderThickness = new Thickness(1)
            };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStack = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
            titleStack.Children.Add(new TextBlock
            {
                Text = AuthorSupportHeaderLine1,
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = AuthorSupportHeaderLine2,
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 2)
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = AuthorSupportHeaderLine3,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = AuthorSupportHeaderLine4,
                FontSize = 11.5,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                TextWrapping = TextWrapping.Wrap
            });

            var actionsStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            var newDrawerBtn = CreateStyledButton("+ Nova Gaveta", Color.FromRgb(37, 99, 235));
            newDrawerBtn.Margin = new Thickness(0, 0, 0, 6);
            newDrawerBtn.Click += delegate
            {
                DrawerModel created = _controller.CreateNewDrawer();
                if (created != null)
                {
                    _selectedDrawerId = created.id;
                    RefreshUI();
                    ShowStatus(string.Format("Gaveta \"{0}\" criada com sucesso!", created.name));
                }
            };

            var copyPixBtn = CreateStyledButton("📋 Copiar PIX", Color.FromRgb(16, 185, 129));
            copyPixBtn.FontSize = 11.5;
            copyPixBtn.Padding = new Thickness(12, 6, 12, 6);
            copyPixBtn.Click += delegate
            {
                try
                {
                    Clipboard.SetText("+55 21 985692631");
                    ShowStatus("Chave PIX (+55 21 985692631 — Renan Santa Rosa Xavier) copiada!");
                }
                catch { }
            };

            actionsStack.Children.Add(newDrawerBtn);
            actionsStack.Children.Add(copyPixBtn);

            Grid.SetColumn(titleStack, 0);
            Grid.SetColumn(actionsStack, 1);
            headerGrid.Children.Add(titleStack);
            headerGrid.Children.Add(actionsStack);
            headerBorder.Child = headerGrid;

            // Main Two-Column Content
            var mainCols = new Grid();
            mainCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            mainCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            mainCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });

            Border leftCard = BuildCatalogPanel();
            Border rightCard = BuildSettingsAndDrawersPanel();

            Grid.SetColumn(leftCard, 0);
            Grid.SetColumn(rightCard, 2);
            mainCols.Children.Add(leftCard);
            mainCols.Children.Add(rightCard);

            // Footer Status Banner
            _statusBanner = new TextBlock
            {
                Text = "Dica: Arraste qualquer Pod até a borda do monitor para transformá-lo em uma Aba Magnética sempre acessível!",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(147, 197, 253)),
                Margin = new Thickness(4, 8, 4, 6)
            };

            // Official Open-Source Security & Authorship Warning Banner at Bottom
            Border bottomWarningCard = CreateOfficialWarningBanner(new Thickness(0));

            Grid.SetRow(headerBorder, 0);
            Grid.SetRow(mainCols, 1);
            Grid.SetRow(_statusBanner, 2);
            Grid.SetRow(bottomWarningCard, 3);
            rootGrid.Children.Add(headerBorder);
            rootGrid.Children.Add(mainCols);
            rootGrid.Children.Add(_statusBanner);
            rootGrid.Children.Add(bottomWarningCard);

            Content = rootGrid;
        }

        private static Border CreateOfficialWarningBanner(Thickness margin)
        {
            var warningBorder = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = margin,
                Background = new SolidColorBrush(Color.FromArgb(45, 245, 158, 11)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(180, 245, 158, 11)),
                BorderThickness = new Thickness(1.2)
            };

            var warningText = new TextBlock
            {
                Text = AuthorOfficialWarningText,
                FontSize = 10.8,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(253, 224, 71)),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Left
            };

            warningBorder.Child = warningText;
            return warningBorder;
        }

        private Border BuildCatalogPanel()
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14),
                Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 148, 163, 184)),
                BorderThickness = new Thickness(1)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 1. Target Drawer Selector
            var targetRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            targetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            targetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            targetRow.Children.Add(new TextBlock
            {
                Text = "Adicionar na Gaveta:",
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            });

            _targetDrawerCombo = new ComboBox
            {
                Height = 30,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _targetDrawerCombo.SelectionChanged += delegate
            {
                var item = _targetDrawerCombo.SelectedItem as ComboBoxItem;
                if (item != null && item.Tag is string)
                {
                    _selectedDrawerId = (string)item.Tag;
                }
            };
            Grid.SetColumn(_targetDrawerCombo, 1);
            targetRow.Children.Add(_targetDrawerCombo);

            // 2. Search Input
            _searchBox = new TextBox
            {
                Height = 32,
                Padding = new Thickness(8, 5, 8, 5),
                FontSize = 12.5,
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            _searchBox.TextChanged += delegate { RenderCatalogItems(); };

            // 3. Filter Buttons
            var filterRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Button btnAll = CreateFilterButton("Todos", "all");
            Button btnDesk = CreateFilterButton("Área de Trabalho", "desktop");
            Button btnStart = CreateFilterButton("Menu Iniciar", "startmenu");
            Button btnRefresh = CreateStyledButton("↻ Atualizar", Color.FromRgb(51, 65, 85));
            btnRefresh.Padding = new Thickness(8, 4, 8, 4);
            btnRefresh.FontSize = 11;
            btnRefresh.Click += delegate
            {
                LoadCatalogData();
                RenderCatalogItems();
                ShowStatus("Catálogo de programas atualizado.");
            };

            filterRow.Children.Add(btnAll);
            filterRow.Children.Add(btnDesk);
            filterRow.Children.Add(btnStart);
            filterRow.Children.Add(btnRefresh);

            // 4. Scrollable App List
            _catalogListPanel = new StackPanel();
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _catalogListPanel
            };

            // 5. Browse Custom File Button (DereferenceLinks = false!)
            var browseBtn = CreateStyledButton("📁 Procurar Arquivo, Executável ou Atalho (.lnk / .url / .exe)...", Color.FromRgb(59, 130, 246));
            browseBtn.Margin = new Thickness(0, 10, 0, 0);
            browseBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
            browseBtn.Click += delegate
            {
                if (!string.IsNullOrEmpty(_selectedDrawerId))
                {
                    _controller.PickFilesForDrawer(_selectedDrawerId);
                    LoadCatalogData();
                    RefreshUI();
                }
            };

            Grid.SetRow(targetRow, 0);
            Grid.SetRow(_searchBox, 1);
            Grid.SetRow(filterRow, 2);
            Grid.SetRow(scroll, 3);
            Grid.SetRow(browseBtn, 4);

            grid.Children.Add(targetRow);
            grid.Children.Add(_searchBox);
            grid.Children.Add(filterRow);
            grid.Children.Add(scroll);
            grid.Children.Add(browseBtn);

            card.Child = grid;
            return card;
        }

        private Button CreateFilterButton(string label, string filterCode)
        {
            var btn = CreateStyledButton(label, Color.FromRgb(51, 65, 85));
            btn.Padding = new Thickness(10, 4, 10, 4);
            btn.Margin = new Thickness(0, 0, 6, 0);
            btn.FontSize = 11;
            btn.Click += delegate
            {
                _sourceFilter = filterCode;
                RenderCatalogItems();
            };
            return btn;
        }

        private Border BuildSettingsAndDrawersPanel()
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14),
                Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 148, 163, 184)),
                BorderThickness = new Thickness(1)
            };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = "⚙️ Configurações do Sistema",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            _startupCheck = new CheckBox
            {
                Content = "Inicializar automaticamente com o Windows (Registro HKCU\\Run)",
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8),
                FontWeight = FontWeights.Medium
            };
            _startupCheck.Click += delegate
            {
                bool val = _startupCheck.IsChecked == true;
                _controller.UpdateSettings(null, null, val, null);
                ShowStatus(val
                    ? "Inicialização com o Windows ATIVADA no Registro do Windows!"
                    : "Inicialização com o Windows desativada.");
            };
            stack.Children.Add(_startupCheck);

            _vaultCheck = new CheckBox
            {
                Content = "Guardar arquivo da Área de Trabalho no Cofre (limpa o Desktop)",
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 12),
                FontWeight = FontWeights.Medium
            };
            _vaultCheck.Click += delegate
            {
                bool val = _vaultCheck.IsChecked == true;
                _controller.UpdateSettings(null, null, null, val);
                ShowStatus(val
                    ? "Atalhos da Área de Trabalho serão guardados na gaveta e restaurados ao remover."
                    : "Atalhos da Área de Trabalho foram mantidos visíveis no Desktop.");
            };
            stack.Children.Add(_vaultCheck);

            // Scale Selector
            stack.Children.Add(new TextBlock
            {
                Text = "Tamanho dos Ícones e Gavetas:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                Margin = new Thickness(0, 0, 0, 4)
            });
            _scaleCombo = new ComboBox { Height = 28, Margin = new Thickness(0, 0, 0, 10) };
            _scaleCombo.Items.Add(new ComboBoxItem { Content = "Sincronizar com o Windows (Automático)", Tag = "auto" });
            _scaleCombo.Items.Add(new ComboBoxItem { Content = "Pequeno (Windows Small — 32px)", Tag = "small" });
            _scaleCombo.Items.Add(new ComboBoxItem { Content = "Médio (Windows Medium — 48px)", Tag = "medium" });
            _scaleCombo.Items.Add(new ComboBoxItem { Content = "Grande (Windows Large — 64px)", Tag = "large" });
            _scaleCombo.SelectionChanged += delegate
            {
                var item = _scaleCombo.SelectedItem as ComboBoxItem;
                if (item != null && item.Tag is string)
                {
                    _controller.UpdateSettings((string)item.Tag, null, null, null);
                }
            };
            stack.Children.Add(_scaleCombo);

            // Theme Selector
            stack.Children.Add(new TextBlock
            {
                Text = "Estilo Visual dos Pods:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                Margin = new Thickness(0, 0, 0, 4)
            });
            _themeCombo = new ComboBox { Height = 28, Margin = new Thickness(0, 0, 0, 16) };
            _themeCombo.Items.Add(new ComboBoxItem { Content = "Azul Discord Translúcido (Padrão)", Tag = "blue" });
            _themeCombo.Items.Add(new ComboBoxItem { Content = "100% Transparente (Minimalista)", Tag = "transparent" });
            _themeCombo.SelectionChanged += delegate
            {
                var item = _themeCombo.SelectedItem as ComboBoxItem;
                if (item != null && item.Tag is string)
                {
                    _controller.UpdateSettings(null, (string)item.Tag, null, null);
                }
            };
            stack.Children.Add(_themeCombo);

            // Drawers Management Section
            stack.Children.Add(new TextBlock
            {
                Text = "🗂️ Suas Gavetas & Bordas Magnéticas",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 4, 0, 8)
            });

            _drawersListPanel = new StackPanel();
            stack.Children.Add(_drawersListPanel);

            stack.Children.Add(CreateOfficialWarningBanner(new Thickness(0, 12, 0, 0)));

            scroll.Content = stack;
            card.Child = scroll;
            return card;
        }

        public void LoadCatalogData()
        {
            _allCatalogEntries = AppCatalogScanner.ScanDefaultSystemShortcuts();
        }

        public void RefreshUI()
        {
            DrawerAppState state = _controller.GetState();
            if (state == null) return;

            // Update settings controls
            _startupCheck.IsChecked = state.settings.launchAtStartup;
            _vaultCheck.IsChecked = state.settings.moveDesktopFilesToVault;

            SelectComboByTag(_scaleCombo, state.settings.iconScaleMode ?? "auto");
            SelectComboByTag(_themeCombo, state.settings.backgroundMode ?? "blue");

            // Populate target drawer combo
            _targetDrawerCombo.Items.Clear();
            if (string.IsNullOrEmpty(_selectedDrawerId) && state.drawers.Count > 0)
            {
                _selectedDrawerId = state.drawers[0].id;
            }

            foreach (DrawerModel d in state.drawers)
            {
                var cbi = new ComboBoxItem
                {
                    Content = string.Format("{0} ({1} itens)", d.name, d.items != null ? d.items.Count : 0),
                    Tag = d.id
                };
                _targetDrawerCombo.Items.Add(cbi);
                if (d.id == _selectedDrawerId)
                {
                    _targetDrawerCombo.SelectedItem = cbi;
                }
            }
            if (_targetDrawerCombo.SelectedItem == null && _targetDrawerCombo.Items.Count > 0)
            {
                _targetDrawerCombo.SelectedIndex = 0;
            }

            RenderCatalogItems();
            RenderDrawersManagementList(state);
        }

        private static void SelectComboByTag(ComboBox combo, string tag)
        {
            foreach (object obj in combo.Items)
            {
                var cbi = obj as ComboBoxItem;
                if (cbi != null && (cbi.Tag as string) == tag)
                {
                    combo.SelectedItem = cbi;
                    return;
                }
            }
        }

        private void RenderCatalogItems()
        {
            _catalogListPanel.Children.Clear();
            if (_allCatalogEntries == null) return;

            string query = (_searchBox.Text ?? "").Trim().ToLowerInvariant();
            int shown = 0;

            foreach (CatalogEntry entry in _allCatalogEntries)
            {
                if (_sourceFilter == "desktop" && !entry.SourceLabel.StartsWith("Área de Trabalho"))
                    continue;
                if (_sourceFilter == "startmenu" && !entry.SourceLabel.StartsWith("Menu Iniciar"))
                    continue;
                if (!string.IsNullOrEmpty(query) &&
                    !entry.Name.ToLowerInvariant().Contains(query) &&
                    !entry.SourceLabel.ToLowerInvariant().Contains(query))
                {
                    continue;
                }

                var rowBorder = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 7, 10, 7),
                    Margin = new Thickness(0, 0, 0, 6),
                    Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(50, 148, 163, 184)),
                    BorderThickness = new Thickness(1)
                };

                var rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                infoStack.Children.Add(new TextBlock
                {
                    Text = entry.Name,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12.5,
                    Foreground = Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                infoStack.Children.Add(new TextBlock
                {
                    Text = string.Format("{0} • {1}", entry.SourceLabel, entry.Extension.ToUpperInvariant()),
                    FontSize = 10.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184))
                });

                CatalogEntry captured = entry;
                Button addBtn = CreateStyledButton("+ Adicionar", Color.FromRgb(37, 99, 235));
                addBtn.Padding = new Thickness(10, 4, 10, 4);
                addBtn.FontSize = 11.5;
                addBtn.Click += delegate
                {
                    if (string.IsNullOrEmpty(_selectedDrawerId)) return;
                    _controller.AddFilesToDrawer(_selectedDrawerId, new string[] { captured.FilePath });
                    LoadCatalogData();
                    RefreshUI();
                    ShowStatus(string.Format("\"{0}\" adicionado à gaveta com sucesso!", captured.Name));
                };

                Grid.SetColumn(infoStack, 0);
                Grid.SetColumn(addBtn, 1);
                rowGrid.Children.Add(infoStack);
                rowGrid.Children.Add(addBtn);
                rowBorder.Child = rowGrid;

                _catalogListPanel.Children.Add(rowBorder);
                shown++;
                if (shown >= 80) break;
            }

            if (shown == 0)
            {
                _catalogListPanel.Children.Add(new TextBlock
                {
                    Text = "Nenhum atalho encontrado para este filtro.",
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    Margin = new Thickness(8)
                });
            }
        }

        private void RenderDrawersManagementList(DrawerAppState state)
        {
            _drawersListPanel.Children.Clear();

            foreach (DrawerModel drawer in state.drawers)
            {
                DrawerModel captured = drawer;
                var box = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 0, 0, 8),
                    Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(65, 96, 165, 250)),
                    BorderThickness = new Thickness(1)
                };

                var stack = new StackPanel();

                // Name + Save Row
                var nameRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var nameBox = new TextBox
                {
                    Text = captured.name,
                    Height = 26,
                    Padding = new Thickness(6, 2, 6, 2),
                    Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                    Margin = new Thickness(0, 0, 6, 0)
                };

                Button saveNameBtn = CreateStyledButton("Salvar", Color.FromRgb(37, 99, 235));
                saveNameBtn.Padding = new Thickness(8, 3, 8, 3);
                saveNameBtn.FontSize = 11;
                saveNameBtn.Margin = new Thickness(0, 0, 4, 0);
                saveNameBtn.Click += delegate
                {
                    _controller.RenameDrawer(captured.id, nameBox.Text);
                    RefreshUI();
                    ShowStatus("Nome da gaveta atualizado.");
                };

                Button delBtn = CreateStyledButton("Excluir", Color.FromRgb(185, 28, 28));
                delBtn.Padding = new Thickness(8, 3, 8, 3);
                delBtn.FontSize = 11;
                delBtn.Click += delegate
                {
                    _controller.DeleteDrawer(captured.id);
                    LoadCatalogData();
                    RefreshUI();
                    ShowStatus("Gaveta excluída e atalhos devolvidos à Área de Trabalho.");
                };

                Grid.SetColumn(nameBox, 0);
                Grid.SetColumn(saveNameBtn, 1);
                Grid.SetColumn(delBtn, 2);
                nameRow.Children.Add(nameBox);
                nameRow.Children.Add(saveNameBtn);
                nameRow.Children.Add(delBtn);
                stack.Children.Add(nameRow);

                // Magnetic Edge Dock Selector Row
                var edgeRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal
                };
                edgeRow.Children.Add(new TextBlock
                {
                    Text = "Posição:",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                });

                edgeRow.Children.Add(CreateQuickEdgeButton(captured, "Desktop", "none"));
                edgeRow.Children.Add(CreateQuickEdgeButton(captured, "Borda Esq.", "left"));
                edgeRow.Children.Add(CreateQuickEdgeButton(captured, "Borda Dir.", "right"));
                edgeRow.Children.Add(CreateQuickEdgeButton(captured, "Topo", "top"));

                stack.Children.Add(edgeRow);
                box.Child = stack;
                _drawersListPanel.Children.Add(box);
            }
        }

        private Button CreateQuickEdgeButton(DrawerModel drawer, string label, string edgeCode)
        {
            string current = string.IsNullOrEmpty(drawer.dockedEdge) ? "none" : drawer.dockedEdge;
            bool active = (current == edgeCode);
            Button btn = CreateStyledButton(
                label,
                active ? Color.FromRgb(37, 99, 235) : Color.FromRgb(51, 65, 85));
            btn.Padding = new Thickness(7, 2, 7, 2);
            btn.Margin = new Thickness(0, 0, 4, 0);
            btn.FontSize = 10.5;
            btn.Click += delegate
            {
                _controller.SetDrawerDockedEdge(drawer.id, edgeCode);
                RefreshUI();
            };
            return btn;
        }

        private void ShowStatus(string message)
        {
            if (_statusBanner != null)
            {
                _statusBanner.Text = message;
            }
        }

        private static Button CreateStyledButton(string text, Color bg)
        {
            return new Button
            {
                Content = text,
                Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(bg),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };
        }
    }
}
