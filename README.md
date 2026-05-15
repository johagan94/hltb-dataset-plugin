# HLTB Dataset Plugin for LaunchBox

A LaunchBox plugin that enriches your game library with **HowLongToBeat** playtime data sourced from a bundled offline dataset (no per-game API calls required).

Displays Main Story / Main + Extras / Completionist times directly in the LaunchBox **Game Details** panel via a custom theme, sitting alongside the existing RetroAchievements *Playtime Commitment* panel.

## Features

- **Offline dataset** — ~166k HLTB entries bundled as CSV, no network calls at runtime
- **Auto-populate on first run** — matches your entire library and writes times to LaunchBox custom fields
- **Smart name matching** — normalization, alias table, and platform-aware variant index for high hit-rates
- **Custom theme** — drop-in `HLTB` LaunchBox theme that adds a *How Long To Beat* card to game details
- **Game menu integration** — right-click a game to re-match, view, or clear HLTB data
- **Manual scraper fallback** — for missing entries, an in-process scraper can fetch directly from howlongtobeat.com

## Repository layout

```
.
├── *.cs                       # Plugin source (C# / .NET 9 Windows)
├── HltbDatasetPlugin.csproj   # Project file (targets net9.0-windows, WPF + WinForms)
├── tests/                     # xUnit test project (50 tests)
├── dataset/                   # Bundled HLTB dataset CSVs
│   ├── hltb_dataset.csv             # Full dataset (~166k rows, ~37 MB)
│   ├── hltb_dataset_filtered.csv    # Filtered/curated subset (~12 MB)
│   └── hltb_dataset_normalized.csv  # Pre-normalized names for matching (~37 MB)
├── LBThemes/HLTB/             # Custom LaunchBox theme (deployed separately)
└── TODO.md                    # Roadmap & known limitations
```

## Build

Requirements:
- Windows 10/11
- .NET 9 SDK
- LaunchBox installed at `C:\Users\Jack\LaunchBox\` (path referenced from `HltbDatasetPlugin.csproj` line 23 — edit `HintPath` to match your install)

```powershell
dotnet build -c Release
```

Outputs to `bin\Release\net9.0-windows\`:
- `HLTB.dll` — plugin assembly
- `hltb_dataset*.csv` — copied alongside the DLL

## Install

1. Copy `HLTB.dll` to `<LaunchBox>\Plugins\HLTB.dll`
2. Copy `HLTB.dll` **also** to `<LaunchBox>\HLTB.dll` (WPF needs it on the app base path for XAML `clr-namespace` resolution)
3. Copy all `hltb_dataset*.csv` into `<LaunchBox>\Plugins\`
4. (Optional) Copy `LBThemes\HLTB\` to `<LaunchBox>\LBThemes\HLTB\` and activate the theme via *Tools → Options → Visuals → Theme = HLTB*

On first launch the plugin auto-matches your library and populates these custom fields per game:
- `HLTB Main Story`
- `HLTB Main + Extras`
- `HLTB Completionist`

Subsequent launches read from the cache; matching is incremental.

## Theme integration

The bundled `HLTB` theme is a fork of LaunchBox's default `GameDetailsView.xaml` with an added "How Long To Beat" card. The card uses two WPF value converters (`HltbFieldConverter`, `HltbHasDataConverter`) which look up the current game's title in `HltbPlugin.DisplayCache` — a static dictionary populated on plugin init and on every `SaveToCustomFields` call.

Without the custom theme the plugin still writes data to LaunchBox custom fields, which are visible in the Edit Game dialog and accessible to any other theme via standard LaunchBox bindings.

## Tests

```powershell
dotnet test tests\HltbDatasetPlugin.Tests.csproj
```

50 tests covering: CSV parsing, name normalization, alias matching, platform disambiguation, time formatting, cache invalidation.

## Dataset

The CSVs in `dataset/` were sourced from a community HLTB scrape dated **2026-03-27** (see `source_url` and `crawled_at` columns).

Columns:
```
id, name, type, platform, genres, developer, publisher,
release_date, release_precision, release_year, release_month, release_day,
main_story_polled, main_story,
main_plus_sides_polled, main_plus_sides,
completionist_polled, completionist,
all_styles_polled, all_styles,
single_player_polled, single_player,
co_op_polled, co_op,
versus_polled, versus,
source_url, crawled_at
```

The dataset is intentionally **point-in-time**. See [TODO.md](TODO.md) for re-scrape plans.

## License

TBD — see TODO.md.

## Credits

- Game times data: [HowLongToBeat](https://howlongtobeat.com/)
- Plugin host: [LaunchBox](https://www.launchbox-app.com/)
