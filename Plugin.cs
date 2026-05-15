using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace HltbDatasetPlugin;

public class HltbPlugin : ISystemEventsPlugin, IGameMultiMenuItemPlugin, ISystemMenuItemPlugin
{
    // STATIC shared state so the 3 plugin-class instances LaunchBox creates
    // (one per interface implemented) all share the same dataset, settings, and scraper.
    private static HltbDataSet? _dataSet;
    private static HltbSettings? _settings;
    private static HltbScraper? _scraper;
    private static bool _initialized;
    private static readonly object _initLock = new();

    /// <summary>Cache of game title → (fieldName → value) for the WPF theme converters.</summary>
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Dictionary<string, string>> DisplayCache = new();

    // ===== ISystemEventsPlugin =====
    // Track which events we've already logged this session to suppress 3x duplicate noise
    // (LaunchBox instantiates this class once per implemented interface).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _seenEvents = new();

    public void OnEventRaised(string eventType)
    {
        try
        {
            // Suppress duplicate logs from multi-instance instantiation
            if (_seenEvents.TryAdd(eventType, 0))
                HltbLogger.Debug($"Event: {eventType}");

            if (eventType == SystemEventTypes.PluginInitialized)
                SafeInit();
            else if (eventType == "GameAdded" || eventType == "GameUpdated" || eventType == "GameRemoved")
            {
                HltbBadge.ClearCache();
            }
        }
        catch (Exception ex)
        {
            HltbLogger.Error("OnEventRaised failed", ex);
        }
    }

    // ===== ISystemMenuItemPlugin (Tools menu) =====
    public string Caption => "HLTB Viewer";
    public Image IconImage => new Bitmap(1, 1);
    public bool ShowInLaunchBox => true;
    public bool ShowInBigBox => true;
    public bool AllowInBigBoxWhenLocked => false;

    public void OnSelected()
    {
        try
        {
            var s = GetSettings();
            var ds = GetDataSet();
            var games = PluginHelper.DataManager.GetAllGames();

            HltbLogger.Info($"Opening HLTB Viewer with {games.Length} games");
            var viewer = new HltbViewer(ds, s, games, GetScraper());
            viewer.ShowDialog();
        }
        catch (Exception ex)
        {
            HltbLogger.Error("Open Viewer failed", ex);
            MessageBox.Show($"Error opening HLTB Viewer: {ex.Message}", "HLTB",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ===== IGameMultiMenuItemPlugin =====
    public IEnumerable<IGameMenuItem> GetMenuItems(params IGame[] selectedGames)
    {
        if (selectedGames == null || selectedGames.Length == 0)
            return Enumerable.Empty<IGameMenuItem>();

        var s = GetSettings();
        var ds = GetDataSet();
        var game = selectedGames[0];

        HltbLogger.Debug($"GetMenuItems: '{game.Title}' [{game.Platform}] ({selectedGames.Length} games selected)");

        // If dataset isn't loaded yet, show a placeholder menu rather than blocking the UI thread
        if (!ds.IsLoaded)
        {
            HltbLogger.Warn("Dataset not yet loaded - showing placeholder menu");
            var loadingSubmenu = new List<IGameMenuItem>
            {
                new GameMenuItem("HLTB dataset still loading, try again in a moment...", null, false)
            };
            return new[] { new GameMenuItem("How Long To Beat (loading...)", null, false, null, loadingSubmenu) };
        }

        var entry = ds.Lookup(game.Title ?? "", game.Platform);
        var items = new List<IGameMenuItem>();

        // === Submenu: "How Long To Beat" with all options nested ===
        var submenu = new List<IGameMenuItem>();

        if (entry != null && entry.HasAnyData)
        {
            // Time info items (disabled, for display only)
            if (entry.HasMainStory)
                submenu.Add(new GameMenuItem($"Main Story: {Etc.FormatTime(entry.MainStory, s.TimeFormat, entry.MainStoryPolled, s.ShowPolledCounts)}", null, false));

            if (entry.HasMainPlusSides)
                submenu.Add(new GameMenuItem($"Main + Extras: {Etc.FormatTime(entry.MainPlusSides, s.TimeFormat, entry.MainPlusSidesPolled, s.ShowPolledCounts)}", null, false));

            if (entry.HasCompletionist)
                submenu.Add(new GameMenuItem($"Completionist: {Etc.FormatTime(entry.Completionist, s.TimeFormat, entry.CompletionistPolled, s.ShowPolledCounts)}", null, false));

            submenu.Add(new GameMenuItem("Add to Custom Fields", games =>
            {
                foreach (var g in games)
                {
                    var e = ds.Lookup(g.Title ?? "", g.Platform);
                    if (e?.HasAnyData == true) SaveToCustomFields(g, e, s);
                }
                PluginHelper.DataManager.Save(true);
                HltbLogger.Info($"Added Custom Fields for {games.Length} game(s) via right-click");
            }));

            submenu.Add(new GameMenuItem("Search HLTB Dataset...", games =>
            {
                foreach (var g in games) ShowSearchDialog(g, ds, s);
            }));
        }
        else
        {
            submenu.Add(new GameMenuItem($"Not found: '{game.Title}'", null, false));

            submenu.Add(new GameMenuItem("Search HLTB Dataset...", games =>
            {
                foreach (var g in games) ShowSearchDialog(g, ds, s);
            }));

            if (s.EnableScraping)
            {
                submenu.Add(new GameMenuItem("Scrape HLTB Live...", games =>
                {
                    foreach (var g in games) ScrapeLive(g, s);
                }));
            }
        }

        // Bulk operations (always available)
        submenu.Add(new GameMenuItem("Bulk Populate All Games (Dataset)", _ =>
        {
            var allGames = PluginHelper.DataManager.GetAllGames();
            BulkPopulate(allGames, ds, s);
        }));

        if (s.EnableScraping)
        {
            submenu.Add(new GameMenuItem("Bulk Populate Missing (Live Scrape)", games =>
            {
                var allGames = PluginHelper.DataManager.GetAllGames();
                Task.Run(() => BulkScrapeMissing(allGames, ds, s));
            }));
        }

        submenu.Add(new GameMenuItem("Open Plugin Log", _ =>
        {
            try { System.Diagnostics.Process.Start("notepad.exe", HltbLogger.LogPath); }
            catch (Exception ex) { HltbLogger.Error("Open log failed", ex); }
        }));

        // Top-level menu item with submenu
        var caption = entry?.HasAnyData == true
            ? $"How Long To Beat: {Etc.FormatTime(entry.MainStory, s.TimeFormat)} / {Etc.FormatTime(entry.MainPlusSides, s.TimeFormat)} / {Etc.FormatTime(entry.Completionist, s.TimeFormat)}"
            : "How Long To Beat";

        items.Add(new GameMenuItem(caption, null, true, null, submenu));

        return items;
    }

    // ===== Init =====
    private void SafeInit()
    {
        // Ensure single global init across all plugin-class instances
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;
            _initialized = true;
        }

        try
        {
            var s = GetSettings();
            HltbLogger.Enabled = s.EnableLogging;
            HltbLogger.Section("Plugin Initializing");
            HltbLogger.Info($"HltbDatasetPlugin v{Assembly.GetExecutingAssembly().GetName().Version} starting");
            HltbLogger.Info($"Plugin dir: {Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)}");
            HltbLogger.Info($"Settings: TimeFormat={s.TimeFormat}, Scraping={s.EnableScraping}, ShowBadge={s.ShowBadge}");

            _ = Task.Run(() =>
            {
                try
                {
                    var ds = GetDataSet();
                    if (!ds.IsLoaded) ds.Load();

                    // Rebuild the WPF display cache from any existing custom fields
                    RebuildDisplayCache();

                    // One-time first-run: auto-populate a batch of games so the panel has data
                    var s = GetSettings();
                    if (!s.FirstRunPopulated)
                    {
                        HltbLogger.Info("First-run populate in progress...");
                        var games = PluginHelper.DataManager.GetAllGames();
                        var count = 0;
                        foreach (var game in games)
                        {
                            try
                            {
                                if (Etc.HasCustomField(game, Etc.FieldMainStory)) continue;
                                var entry = ds.Lookup(game.Title ?? "", game.Platform);
                                if (entry?.HasAnyData == true)
                                {
                                    SaveToCustomFields(game, entry, s);
                                    count++;
                                }
                            }
                            catch { }
                        }
                        try
                        {
                            PluginHelper.DataManager.Save(true);
                            s.FirstRunPopulated = true;
                            s.Save();
                            HltbLogger.Info($"First-run populate complete: {count} games populated");
                        }
                        catch (Exception ex)
                        {
                            HltbLogger.Error("First-run populate save failed", ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    HltbLogger.Error("Background dataset load failed", ex);
                }
            });
        }
        catch (Exception ex)
        {
            HltbLogger.Error("SafeInit failed", ex);
        }
    }

    // ===== Shared accessors =====
    private static HltbDataSet GetDataSet()
    {
        if (_dataSet != null) return _dataSet;
        lock (_initLock)
        {
            if (_dataSet == null)
            {
                var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
                _dataSet = new HltbDataSet(dir);
            }
        }
        return _dataSet;
    }

    private static HltbSettings GetSettings()
    {
        if (_settings != null) return _settings;
        lock (_initLock)
        {
            if (_settings != null) return _settings;
            try
            {
                var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
                var path = Path.Combine(dir, "hltb_settings.json");
                _settings = HltbSettings.LoadOrCreate(path);
            }
            catch (Exception ex)
            {
                HltbLogger.Warn($"Settings load failed, using defaults: {ex.Message}");
                _settings = new HltbSettings();
            }
        }
        return _settings;
    }

    private static HltbScraper GetScraper()
    {
        if (_scraper != null) return _scraper;
        lock (_initLock)
        {
            return _scraper ??= new HltbScraper();
        }
    }

    /// <summary>Rebuild the WPF display cache from existing custom fields on all games.</summary>
    private static void RebuildDisplayCache()
    {
        try
        {
            var games = PluginHelper.DataManager.GetAllGames();
            var count = 0;
            foreach (var game in games)
            {
                try
                {
                    var title = game.Title ?? "";
                    if (string.IsNullOrEmpty(title)) continue;

                    var fields = game.GetAllCustomFields();
                    bool hasHltb = false;
                    var dict = new Dictionary<string, string>();

                    foreach (var f in fields)
                    {
                        if (f.Name == Etc.FieldMainStory || f.Name == Etc.FieldMainPlusExtras || f.Name == Etc.FieldCompletionist)
                        {
                            dict[f.Name] = f.Value ?? "";
                            hasHltb = true;
                        }
                    }

                    if (hasHltb)
                    {
                        DisplayCache[title] = dict;
                        count++;
                    }
                }
                catch { }
            }
            HltbLogger.Info($"DisplayCache rebuilt: {count} games indexed");
        }
        catch (Exception ex)
        {
            HltbLogger.Error("DisplayCache rebuild failed", ex);
        }
    }

    private static void SaveToCustomFields(IGame game, HltbEntry entry, HltbSettings settings)
    {
        var title = game.Title ?? "";
        var fields = DisplayCache.GetOrAdd(title, _ => new Dictionary<string, string>());

        if (entry.HasMainStory)
        {
            var val = Etc.FormatTime(entry.MainStory, settings.TimeFormat, entry.MainStoryPolled, settings.ShowPolledCounts);
            Etc.SetCustomField(game, Etc.FieldMainStory, val);
            fields[Etc.FieldMainStory] = val;
        }

        if (entry.HasMainPlusSides)
        {
            var val = Etc.FormatTime(entry.MainPlusSides, settings.TimeFormat, entry.MainPlusSidesPolled, settings.ShowPolledCounts);
            Etc.SetCustomField(game, Etc.FieldMainPlusExtras, val);
            fields[Etc.FieldMainPlusExtras] = val;
        }

        if (entry.HasCompletionist)
        {
            var val = Etc.FormatTime(entry.Completionist, settings.TimeFormat, entry.CompletionistPolled, settings.ShowPolledCounts);
            Etc.SetCustomField(game, Etc.FieldCompletionist, val);
            fields[Etc.FieldCompletionist] = val;
        }

        // Invalidate badge cache so the badge re-evaluates this game
        HltbBadge.InvalidateCache(game.Id);
    }

    private void BulkPopulate(IGame[] games, HltbDataSet ds, HltbSettings settings)
    {
        HltbLogger.Section($"Bulk Populate (Dataset): {games.Length} games");
        var count = 0;
        var skipped = 0;
        var missed = 0;

        foreach (var game in games)
        {
            try
            {
                if (Etc.HasCustomField(game, Etc.FieldMainStory)) { skipped++; continue; }

                var entry = ds.Lookup(game.Title ?? "", game.Platform);
                if (entry != null && entry.HasAnyData)
                {
                    SaveToCustomFields(game, entry, settings);
                    count++;
                }
                else missed++;
            }
            catch (Exception ex)
            {
                HltbLogger.Error($"Bulk populate failed for '{game.Title}'", ex);
            }
        }

        try { PluginHelper.DataManager.Save(true); } catch { }

        HltbLogger.Info($"Bulk populate done: populated={count}, skipped(existing)={skipped}, missed={missed}");
        MessageBox.Show($"Bulk Populate complete:\n  Populated: {count}\n  Skipped (already had data): {skipped}\n  No match found: {missed}\n\nSee log: {HltbLogger.LogPath}",
            "HLTB Dataset", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void BulkScrapeMissing(IGame[] games, HltbDataSet ds, HltbSettings settings)
    {
        HltbLogger.Section($"Bulk Scrape Missing: {games.Length} games");
        var scraper = GetScraper();
        var count = 0;
        var scrapeAttempts = 0;
        var failures = 0;

        foreach (var game in games)
        {
            try
            {
                if (Etc.HasCustomField(game, Etc.FieldMainStory)) continue;

                var entry = ds.Lookup(game.Title ?? "", game.Platform);
                if (entry?.HasAnyData != true)
                {
                    scrapeAttempts++;
                    entry = scraper.SearchAsync(game.Title ?? "").GetAwaiter().GetResult();
                }

                if (entry?.HasAnyData == true)
                {
                    SaveToCustomFields(game, entry, settings);
                    count++;
                }
                else failures++;
            }
            catch (Exception ex)
            {
                HltbLogger.Error($"Bulk scrape failed for '{game.Title}'", ex);
                failures++;
            }
        }

        try { PluginHelper.DataManager.Save(true); } catch (Exception ex) { HltbLogger.Debug($"Save after bulk scrape failed: {ex.Message}"); }

        HltbLogger.Info($"Bulk scrape done: populated={count}, scrape_attempts={scrapeAttempts}, failures={failures}");
        MessageBox.Show($"Bulk Scrape complete:\n  Populated: {count}\n  Scrape attempts: {scrapeAttempts}\n  Failures: {failures}\n\nSee log: {HltbLogger.LogPath}",
            "HLTB Dataset", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ScrapeLive(IGame game, HltbSettings settings)
    {
        HltbLogger.Info($"Manual scrape requested for '{game.Title}'");

        // Run on a background thread to avoid freezing LaunchBox's UI.
        // The scrape can take up to 10 seconds (HTTP timeout) + retry overhead.
        Task.Run(async () =>
        {
            try
            {
                var scraper = GetScraper();
                var entry = await scraper.SearchAsync(game.Title ?? "").ConfigureAwait(false);

                // Marshal UI work back to the UI thread (LaunchBox is WPF)
                Etc.InvokeOnUi(() =>
                {
                    if (entry?.HasAnyData == true)
                    {
                        SaveToCustomFields(game, entry, settings);
                        try { PluginHelper.DataManager.Save(true); } catch (Exception sx) { HltbLogger.Error("Save after scrape failed", sx); }
                        MessageBox.Show($"Found via HLTB live:\n\n{entry.Name}\nMain: {Etc.FormatTime(entry.MainStory, settings.TimeFormat)}\nExtras: {Etc.FormatTime(entry.MainPlusSides, settings.TimeFormat)}\nCompletionist: {Etc.FormatTime(entry.Completionist, settings.TimeFormat)}\n\nSaved to Custom Fields.",
                            "HLTB Scrape", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show($"No HLTB result for '{game.Title}'.\nCheck log: {HltbLogger.LogPath}",
                            "HLTB Scrape", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                });
            }
            catch (Exception ex)
            {
                HltbLogger.Error($"Manual scrape failed for '{game.Title}'", ex);
                Etc.InvokeOnUi(() =>
                    MessageBox.Show($"Scrape error: {ex.Message}", "HLTB Scrape",
                        MessageBoxButtons.OK, MessageBoxIcon.Error));
            }
        });
    }

    private void ShowSearchDialog(IGame game, HltbDataSet ds, HltbSettings settings)
    {
        var results = ds.Search(game.Title ?? "", 30);
        HltbLogger.Debug($"ShowSearchDialog: '{game.Title}' returned {results.Count} candidates");

        if (results.Count == 0)
        {
            if (settings.EnableScraping)
            {
                var choice = MessageBox.Show($"No dataset matches for \"{game.Title}\".\n\nWould you like to scrape HLTB live?",
                    "HLTB Search", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (choice == DialogResult.Yes)
                    ScrapeLive(game, settings);
            }
            else
            {
                MessageBox.Show($"No matches found for \"{game.Title}\".", "HLTB Search",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return;
        }

        using var form = new Form
        {
            Text = $"HLTB Search: {game.Title}",
            Width = 760,
            Height = 520,
            FormBorderStyle = FormBorderStyle.Sizable,
            StartPosition = FormStartPosition.CenterScreen
        };

        var listBox = new ListBox
        {
            Left = 10, Top = 10, Width = 720, Height = 410,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };

        foreach (var entry in results)
        {
            var text = $"{entry.Name} [{entry.Platform}]";
            if (entry.HasMainStory)
                text += $" | Main: {Etc.FormatTime(entry.MainStory, settings.TimeFormat)}";
            if (entry.HasMainPlusSides)
                text += $" | Extras: {Etc.FormatTime(entry.MainPlusSides, settings.TimeFormat)}";
            if (entry.HasCompletionist)
                text += $" | Compl: {Etc.FormatTime(entry.Completionist, settings.TimeFormat)}";

            listBox.Items.Add(new SearchResultItem(text, entry));
        }
        listBox.DisplayMember = nameof(SearchResultItem.Display);
        listBox.SelectedIndex = 0;

        var selectBtn = new System.Windows.Forms.Button
        {
            Text = "Save to Custom Fields",
            Left = 10, Top = 430, Width = 200,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };

        var scrapeBtn = new System.Windows.Forms.Button
        {
            Text = "Scrape HLTB Live...",
            Left = 220, Top = 430, Width = 150,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Visible = settings.EnableScraping
        };

        var cancelBtn = new System.Windows.Forms.Button
        {
            Text = "Cancel",
            Left = 630, Top = 430, Width = 100,
            DialogResult = DialogResult.Cancel,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };

        selectBtn.Click += (_, _) =>
        {
            if (listBox.SelectedItem is SearchResultItem item)
            {
                SaveToCustomFields(game, item.Entry, settings);
                try { PluginHelper.DataManager.Save(true); } catch (Exception ex) { HltbLogger.Debug($"Save after search dialog failed: {ex.Message}"); }
                HltbLogger.Info($"User selected '{item.Entry.Name}' for '{game.Title}' from search dialog");
                form.DialogResult = DialogResult.OK;
                form.Close();
            }
        };

        scrapeBtn.Click += (_, _) =>
        {
            form.DialogResult = DialogResult.OK;
            form.Close();
            ScrapeLive(game, settings);
        };

        form.Controls.Add(listBox);
        form.Controls.Add(selectBtn);
        form.Controls.Add(scrapeBtn);
        form.Controls.Add(cancelBtn);
        form.AcceptButton = selectBtn;
        form.CancelButton = cancelBtn;

        form.ShowDialog();
    }

    private class SearchResultItem
    {
        public string Display { get; }
        public HltbEntry Entry { get; }
        public SearchResultItem(string display, HltbEntry entry)
        {
            Display = display;
            Entry = entry;
        }
        public override string ToString() => Display;
    }
}
