using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace HltbDatasetPlugin;

public class HltbViewer : Window
{
    private readonly HltbDataSet _dataSet;
    private readonly HltbSettings _settings;
    private readonly IGame[] _allGames;
    private readonly HltbScraper? _scraper;
    private DataGrid _grid = null!;
    private System.Windows.Controls.TextBlock _detailTitle = null!;
    private StackPanel _detailBars = null!;
    private System.Windows.Controls.ComboBox _platformFilter = null!;
    private System.Windows.Controls.ComboBox _sortCombo = null!;

    private string? _selectedGameId;

    private static readonly SolidColorBrush MainStoryColor = new(System.Windows.Media.Color.FromRgb(0, 139, 139));
    private static readonly SolidColorBrush MainExtraColor = new(System.Windows.Media.Color.FromRgb(65, 105, 225));
    private static readonly SolidColorBrush CompletionistColor = new(System.Windows.Media.Color.FromRgb(34, 139, 34));

    public HltbViewer(HltbDataSet dataSet, HltbSettings settings, IGame[] allGames, HltbScraper? scraper = null)
    {
        _dataSet = dataSet;
        _settings = settings;
        _allGames = allGames;
        _scraper = scraper;

        Title = "HLTB Viewer";
        Width = 1000;
        Height = 650;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResize;
        MinWidth = 700;
        MinHeight = 450;

        BuildUI();
        RefreshGrid();
    }

    private void BuildUI()
    {
        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(150) });

        // ---- Toolbar ----
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5) };

        _platformFilter = new System.Windows.Controls.ComboBox { Width = 180, Margin = new Thickness(0, 0, 10, 0) };
        _platformFilter.Items.Add(new ComboBoxItem { Content = "All Platforms", Tag = "" });
        var platforms = _allGames
            .Select(g => g.Platform)
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct()
            .OrderBy(p => p);
        foreach (var p in platforms)
            _platformFilter.Items.Add(new ComboBoxItem { Content = p, Tag = p });
        _platformFilter.SelectedIndex = 0;
        _platformFilter.SelectionChanged += (_, _) => RefreshGrid();

        _sortCombo = new System.Windows.Controls.ComboBox { Width = 140, Margin = new Thickness(0, 0, 10, 0) };
        _sortCombo.Items.Add(new ComboBoxItem { Content = "Title", Tag = "Title" });
        _sortCombo.Items.Add(new ComboBoxItem { Content = "Main Story", Tag = "Main" });
        _sortCombo.Items.Add(new ComboBoxItem { Content = "Main + Extras", Tag = "Extra" });
        _sortCombo.Items.Add(new ComboBoxItem { Content = "Completionist", Tag = "Completionist" });
        _sortCombo.SelectedIndex = 0;
        _sortCombo.SelectionChanged += (_, _) => RefreshGrid();

        var refreshBtn = new System.Windows.Controls.Button { Content = "Refresh", Width = 80, Margin = new Thickness(0, 0, 10, 0) };
        refreshBtn.Click += (_, _) => RefreshGrid();

        var playlistBtn = new System.Windows.Controls.Button { Content = "Create Playlist", Width = 110, Margin = new Thickness(0, 0, 10, 0) };
        playlistBtn.Click += (_, _) => CreatePlaylist();

        var bulkBtn = new System.Windows.Controls.Button { Content = "Bulk Populate All", Width = 130 };
        bulkBtn.Click += (_, _) =>
        {
            var games = _allGames.Where(g => !Etc.HasCustomField(g, Etc.FieldMainStory)).ToArray();
            var count = 0;
            var errors = 0;
            foreach (var game in games)
            {
                try
                {
                    var entry = _dataSet.Lookup(game.Title ?? "", game.Platform);
                    if (entry?.HasAnyData == true)
                    {
                        SaveToCustomFields(game, entry);
                        count++;
                    }
                }
                catch (Exception ex)
                {
                    errors++;
                    HltbLogger.Debug($"Bulk populate failed for '{game.Title}': {ex.Message}");
                }
            }
            try { PluginHelper.DataManager.Save(true); } catch (Exception ex) { HltbLogger.Debug($"Save after bulk populate failed: {ex.Message}"); }
            RefreshGrid();
            System.Windows.MessageBox.Show($"Populated HLTB data for {count} game(s).{(errors > 0 ? $" ({errors} errors)" : "")}", "HLTB Dataset",
                MessageBoxButton.OK, MessageBoxImage.Information);
        };

        toolbar.Children.Add(new System.Windows.Controls.TextBlock { Text = "Platform:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
        toolbar.Children.Add(_platformFilter);
        toolbar.Children.Add(new System.Windows.Controls.TextBlock { Text = "Sort:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
        toolbar.Children.Add(_sortCombo);
        toolbar.Children.Add(refreshBtn);
        toolbar.Children.Add(playlistBtn);
        toolbar.Children.Add(bulkBtn);

        Grid.SetRow(toolbar, 0);

        // ---- DataGrid ----
        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            Margin = new Thickness(5)
        };

        _grid.Columns.Add(new DataGridTextColumn { Header = "Title", Binding = new System.Windows.Data.Binding("Title"), Width = new DataGridLength(250) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Platform", Binding = new System.Windows.Data.Binding("Platform"), Width = new DataGridLength(130) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Main Story", Binding = new System.Windows.Data.Binding("MainStory"), Width = new DataGridLength(100) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Main + Extras", Binding = new System.Windows.Data.Binding("MainExtra"), Width = new DataGridLength(100) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Completionist", Binding = new System.Windows.Data.Binding("Completionist"), Width = new DataGridLength(100) });

        _grid.SelectionChanged += OnGridSelectionChanged;
        _grid.MouseDoubleClick += (_, _) => LaunchSelectedGame();

        Grid.SetRow(_grid, 1);

        // ---- Detail Panel ----
        var detailPanel = new Border
        {
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 60)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30)),
            Margin = new Thickness(0)
        };

        var detailContent = new Grid { Margin = new Thickness(10) };
        detailContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        detailContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });

        var detailLeft = new StackPanel();
        _detailTitle = new System.Windows.Controls.TextBlock { FontSize = 16, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 10) };
        _detailBars = new StackPanel();

        detailLeft.Children.Add(_detailTitle);
        detailLeft.Children.Add(_detailBars);

        var detailRight = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var launchBtn = new System.Windows.Controls.Button { Content = "Launch Game", Width = 100, Height = 30, Margin = new Thickness(0, 0, 0, 10) };
        launchBtn.Click += (_, _) => LaunchSelectedGame();

        var playlistBtn2 = new System.Windows.Controls.Button { Content = "Add to Playlist", Width = 100, Height = 30 };
        playlistBtn2.Click += (_, _) => CreatePlaylist();

        detailRight.Children.Add(launchBtn);
        detailRight.Children.Add(playlistBtn2);

        Grid.SetColumn(detailLeft, 0);
        Grid.SetColumn(detailRight, 1);
        detailContent.Children.Add(detailLeft);
        detailContent.Children.Add(detailRight);
        detailPanel.Child = detailContent;

        Grid.SetRow(detailPanel, 2);

        mainGrid.Children.Add(toolbar);
        mainGrid.Children.Add(_grid);
        mainGrid.Children.Add(detailPanel);

        Content = mainGrid;
    }

    private void RefreshGrid()
    {
        var platformFilter = (_platformFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        var sortField = (_sortCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Title";

        var rows = new List<GameRow>();

        foreach (var game in _allGames)
        {
            try
            {
                if (!string.IsNullOrEmpty(platformFilter) &&
                    !string.Equals(game.Platform, platformFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var main = GetCustomField(game, Etc.FieldMainStory);
                var extra = GetCustomField(game, Etc.FieldMainPlusExtras);
                var comp = GetCustomField(game, Etc.FieldCompletionist);

                if (string.IsNullOrEmpty(main) && string.IsNullOrEmpty(extra) && string.IsNullOrEmpty(comp))
                    continue;

                rows.Add(new GameRow
                {
                    GameId = game.Id ?? "",
                    Title = game.Title ?? "",
                    Platform = game.Platform ?? "",
                    MainStory = main,
                    MainExtra = extra,
                    Completionist = comp,
                    MainStorySort = Etc.TimeToMinutes(main),
                    MainExtraSort = Etc.TimeToMinutes(extra),
                    CompletionistSort = Etc.TimeToMinutes(comp)
                });
            }
            catch { }
        }

        rows = sortField switch
        {
            "Main" => rows.OrderBy(r => r.MainStorySort).ThenBy(r => r.Title).ToList(),
            "Extra" => rows.OrderBy(r => r.MainExtraSort).ThenBy(r => r.Title).ToList(),
            "Completionist" => rows.OrderBy(r => r.CompletionistSort).ThenBy(r => r.Title).ToList(),
            _ => rows.OrderBy(r => r.Title).ToList()
        };

        _grid.ItemsSource = rows;

        if (_selectedGameId != null)
        {
            var match = rows.FirstOrDefault(r => r.GameId == _selectedGameId);
            if (match != null)
                _grid.SelectedItem = match;
        }
    }

    private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_grid.SelectedItem is not GameRow row) return;
        _selectedGameId = row.GameId;

        _detailTitle.Text = $"{row.Title}  [{row.Platform}]";
        _detailBars.Children.Clear();

        var totalMax = Math.Max(Math.Max(row.MainStorySort, row.MainExtraSort), row.CompletionistSort);
        if (totalMax == 0) totalMax = 1;

        AddTimeBar("Main Story", row.MainStory, row.MainStorySort, totalMax, MainStoryColor);
        AddTimeBar("Main + Extras", row.MainExtra, row.MainExtraSort, totalMax, MainExtraColor);
        AddTimeBar("Completionist", row.Completionist, row.CompletionistSort, totalMax, CompletionistColor);
    }

    private void AddTimeBar(string label, string time, int minutes, int totalMax, SolidColorBrush color)
    {
        if (string.IsNullOrEmpty(time) || minutes == 0) return;

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };

        panel.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Width = 16, Height = 16,
            Fill = color,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = label + ":",
            Width = 120,
            Foreground = System.Windows.Media.Brushes.LightGray,
            VerticalAlignment = VerticalAlignment.Center
        });

        var barContainer = new Border
        {
            Width = 300, Height = 20,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 50, 50)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 10, 0),
            CornerRadius = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Center
        };

        var barWidth = Math.Max(10, (int)(300.0 * minutes / totalMax));
        var bar = new Border
        {
            Width = barWidth, Height = 18,
            Background = color,
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };

        barContainer.Child = bar;
        panel.Children.Add(barContainer);

        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = time,
            Foreground = System.Windows.Media.Brushes.White,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });

        _detailBars.Children.Add(panel);
    }

    private void LaunchSelectedGame()
    {
        if (_grid.SelectedItem is not GameRow row) return;
        try
        {
            var game = PluginHelper.DataManager.GetGameById(row.GameId);
            if (game != null)
                game.Play();
        }
        catch (Exception ex)
        {
            HltbLogger.Debug($"Launch game failed for '{row.Title}': {ex.Message}");
        }
    }

    private void CreatePlaylist()
    {
        try
        {
            var inputDialog = new System.Windows.Forms.Form
            {
                Text = "Create Playlist",
                Width = 400,
                Height = 200,
                FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen
            };

            var lbl = new System.Windows.Forms.Label { Text = "Playlist Name:", Left = 10, Top = 10, Width = 360 };
            var txt = new System.Windows.Forms.TextBox { Text = "HLTB Games", Left = 10, Top = 35, Width = 360 };
            var ok = new System.Windows.Forms.Button { Text = "Create", Left = 200, Top = 70, Width = 80, DialogResult = System.Windows.Forms.DialogResult.OK };
            var cancel = new System.Windows.Forms.Button { Text = "Cancel", Left = 290, Top = 70, Width = 80, DialogResult = System.Windows.Forms.DialogResult.Cancel };

            inputDialog.Controls.Add(lbl);
            inputDialog.Controls.Add(txt);
            inputDialog.Controls.Add(ok);
            inputDialog.Controls.Add(cancel);
            inputDialog.AcceptButton = ok;
            inputDialog.CancelButton = cancel;

            if (inputDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            var playlistName = txt.Text.Trim();
            if (string.IsNullOrEmpty(playlistName)) return;

            var playlist = PluginHelper.DataManager.AddNewPlaylist(playlistName);

            if (_grid.ItemsSource is IEnumerable<GameRow> rows)
            {
                foreach (var row in rows)
                {
                    try
                    {
                        var game = PluginHelper.DataManager.GetGameById(row.GameId);
                        if (game != null)
                        {
                            var pg = playlist.AddNewPlaylistGame();
                            pg.GameId = game.Id;
                        }
                    }
                    catch (Exception ex)
                    {
                        HltbLogger.Debug($"Playlist add failed for '{row.Title}': {ex.Message}");
                    }
                }
            }

            PluginHelper.DataManager.Save(true);
            System.Windows.MessageBox.Show($"Playlist \"{playlistName}\" created.", "HLTB Dataset",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            HltbLogger.Error("CreatePlaylist failed", ex);
            System.Windows.MessageBox.Show($"Error creating playlist: {ex.Message}", "HLTB Dataset",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveToCustomFields(IGame game, HltbEntry entry)
    {
        if (entry.HasMainStory)
            Etc.SetCustomField(game, Etc.FieldMainStory,
                Etc.FormatTime(entry.MainStory, _settings.TimeFormat, entry.MainStoryPolled, _settings.ShowPolledCounts));

        if (entry.HasMainPlusSides)
            Etc.SetCustomField(game, Etc.FieldMainPlusExtras,
                Etc.FormatTime(entry.MainPlusSides, _settings.TimeFormat, entry.MainPlusSidesPolled, _settings.ShowPolledCounts));

        if (entry.HasCompletionist)
            Etc.SetCustomField(game, Etc.FieldCompletionist,
                Etc.FormatTime(entry.Completionist, _settings.TimeFormat, entry.CompletionistPolled, _settings.ShowPolledCounts));
    }

    private static string GetCustomField(IGame game, string name)
    {
        return game.GetAllCustomFields().FirstOrDefault(f => f.Name == name)?.Value ?? "";
    }
}

internal class GameRow
{
    public string GameId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Platform { get; set; } = "";
    public string MainStory { get; set; } = "";
    public string MainExtra { get; set; } = "";
    public string Completionist { get; set; } = "";
    public int MainStorySort { get; set; }
    public int MainExtraSort { get; set; }
    public int CompletionistSort { get; set; }
}
