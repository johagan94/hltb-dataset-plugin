# TODO / Roadmap

## Pre-release cleanup (before GitHub publish)
- [ ] Choose a license (MIT / GPL / etc.) and add `LICENSE` file
- [ ] Make `HintPath` for `Unbroken.LaunchBox.Plugins.dll` configurable (env var or relative LaunchBox path) instead of hard-coded `C:\Users\Jack\LaunchBox\`
- [ ] Strip personal paths from logs / debug strings
- [ ] Add screenshots of the theme panel to the README
- [ ] CI build (GitHub Actions) producing a release `.zip` with `HLTB.dll` + dataset + theme
- [ ] Decide whether to ship `hltb_dataset_normalized.csv` (currently unused at runtime — only the full + filtered CSVs are loaded; the normalized one is generated for reference)
- [ ] Consider Git LFS for the dataset CSVs if the repo grows further

## Dataset maintenance
- [ ] **Re-scrape the HLTB dataset periodically** — the bundled snapshot is from 2026-03-27 and will go stale. Either:
  - Re-run the original scraper (not included — was external) against howlongtobeat.com, or
  - Find / consume a maintained community dataset and standardize on its schema, or
  - Investigate alternatives such as [HowLongToBeat-Steam-Integration](https://github.com/ckatzorke/howlongtobeat) wrappers, IGDB time-to-beat fields, or PCGamingWiki playtime data as fallbacks/supplements
- [ ] Add a CLI tool / GitHub Action that produces a fresh dataset on a schedule and opens a PR with the updated CSVs
- [ ] Track dataset version (date) in plugin UI so users can see how stale their data is

## Plugin features
- [ ] Settings UI for: enable/disable auto-populate, choose which fields to write, dataset path override
- [ ] Per-game override (manually pick the correct HLTB entry when auto-match is wrong)
- [ ] Progress bar / dialog during first-run population (currently silent background)
- [ ] Optional integration with LaunchBox's built-in "Play Time" field (compare HLTB vs your actual playtime)
- [ ] Surface additional HLTB columns where useful: `all_styles`, `single_player`, `co_op`, `versus`

## Known limitations
- Match rate isn't 100% — region/edition variants, fan translations, and ROM hacks frequently miss. Alias table in `KnownAliases.cs` covers common cases but is hand-maintained.
- Theme requires `HLTB.dll` in the LaunchBox root directory (in addition to `Plugins\`) because WPF resolves `clr-namespace` against the host app's base path, not the plugins dir.
- LaunchBox's game details `DataContext` does not expose game `Id` — matching by `Title` only. Two distinct games with identical titles on different platforms will currently share whichever entry was last written.
