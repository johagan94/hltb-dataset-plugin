using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HltbDatasetPlugin;

public class HltbDataSet
{
    private readonly string _pluginDir;
    private List<HltbEntry> _entries = new();
    private Dictionary<string, List<HltbEntry>> _nameIndex = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<HltbEntry>> _variantIndex = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public bool IsLoaded => _loaded;
    public int EntryCount => _entries.Count;

    public HltbDataSet(string pluginDir)
    {
        _pluginDir = pluginDir;
    }

    public void Load()
    {
        if (_loaded) return;
        var start = DateTime.UtcNow;
        HltbLogger.Section($"CSV Load: {_pluginDir}");

        LoadCsv("hltb_dataset_filtered.csv");
        LoadCsv("hltb_dataset.csv");
        BuildIndex();
        _loaded = true;

        var elapsed = DateTime.UtcNow - start;
        HltbLogger.Info($"CSV Load complete: {_entries.Count} entries, {_nameIndex.Count} normalized names, {_variantIndex.Count} variant keys ({elapsed.TotalSeconds:F2}s)");
    }

    public async Task LoadAsync()
    {
        if (_loaded) return;
        await Task.Run(Load);
    }

    private void LoadCsv(string filename)
    {
        var path = Path.Combine(_pluginDir, filename);
        if (!File.Exists(path))
        {
            HltbLogger.Warn($"CSV not found: {path}");
            return;
        }

        var beforeCount = _entries.Count;
        try
        {
            var seen = new HashSet<string>(_entries.Select(e => e.Id));
            using var reader = new StreamReader(path, Encoding.UTF8);

            reader.ReadLine(); // skip header

            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (string.IsNullOrEmpty(line)) continue;

                try
                {
                    var entry = ParseLine(line);
                    if (entry != null && seen.Add(entry.Id))
                    {
                        entry.NormalizedName = NameMatcher.Normalize(entry.Name);
                        entry.PlatformList = entry.Platform
                            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(p => p.Trim())
                            .ToArray();
                        _entries.Add(entry);
                    }
                }
                catch (Exception ex)
                {
                    HltbLogger.Debug($"CSV parse error on line: {ex.Message}");
                }
            }

            HltbLogger.Info($"Loaded {_entries.Count - beforeCount} entries from {filename}");
        }
        catch (Exception ex)
        {
            HltbLogger.Error($"CSV load error ({filename})", ex);
        }
    }

    private void BuildIndex()
    {
        _nameIndex.Clear();
        _variantIndex.Clear();

        foreach (var entry in _entries)
        {
            if (string.IsNullOrEmpty(entry.NormalizedName)) continue;

            // Primary normalized name index (full name, no suffix stripping)
            AddToIndex(_nameIndex, entry.NormalizedName, entry);

            // Also add the suffix-stripped form to the variant index
            // E.g. "Final Fantasy VII Remake" → variant key "final fantasy vii"
            var stripped = NameMatcher.StripEditionSuffix(entry.NormalizedName);
            if (stripped != entry.NormalizedName && !string.IsNullOrEmpty(stripped))
                AddToIndex(_variantIndex, stripped, entry);

            // Variant index: for dual-release titles, index each component
            var lookupKeys = NameMatcher.GenerateLookupKeys(entry.Name);
            foreach (var key in lookupKeys)
            {
                if (key == entry.NormalizedName) continue; // already in primary
                AddToIndex(_variantIndex, key, entry);
            }
        }
    }

    private static void AddToIndex(Dictionary<string, List<HltbEntry>> index, string key, HltbEntry entry)
    {
        if (!index.TryGetValue(key, out var list))
        {
            list = new List<HltbEntry>();
            index[key] = list;
        }
        if (!list.Contains(entry))
            list.Add(entry);
    }

    /// <summary>Parse a single CSV line into an HltbEntry. Handles quoted fields with commas.</summary>
    private static HltbEntry? ParseLine(string line)
    {
        var fields = SplitCsvLine(line);
        if (fields.Length < 20) return null;

        return new HltbEntry
        {
            Id = fields[0],
            Name = fields[1],
            Type = fields[2],
            Platform = fields[3],
            Genres = fields[4],
            Developer = fields[5],
            Publisher = fields[6],
            ReleaseDate = fields[7],
            ReleaseYear = ParseInt(fields[9]),
            ReleaseMonth = ParseInt(fields[10]),
            ReleaseDay = ParseInt(fields[11]),
            MainStoryPolled = ParseInt(fields[12]),
            MainStory = ParseDouble(fields[13]),
            MainPlusSidesPolled = ParseInt(fields[14]),
            MainPlusSides = ParseDouble(fields[15]),
            CompletionistPolled = ParseInt(fields[16]),
            Completionist = ParseDouble(fields[17]),
            AllStylesPolled = ParseInt(fields[18]),
            AllStyles = ParseDouble(fields[19]),
            SinglePlayerPolled = fields.Length > 20 ? ParseInt(fields[20]) : 0,
            SinglePlayer = fields.Length > 21 ? ParseDouble(fields[21]) : 0,
            CoOpPolled = fields.Length > 22 ? ParseInt(fields[22]) : 0,
            CoOp = fields.Length > 23 ? ParseDouble(fields[23]) : 0,
            VersusPolled = fields.Length > 24 ? ParseInt(fields[24]) : 0,
            Versus = fields.Length > 25 ? ParseDouble(fields[25]) : 0,
            SourceUrl = fields.Length > 26 ? fields[26] : ""
        };
    }

    private static string[] SplitCsvLine(string line)
    {
        var result = new List<string>();
        var inQuotes = false;
        var current = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                // Handle escaped double-quote ""
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString());
        return result.ToArray();
    }

    private static int ParseInt(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        return int.TryParse(s, out var v) ? v : 0;
    }

    private static double ParseDouble(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        return double.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    /// <summary>Look up a game by title + platform. Returns the best match or null.</summary>
    public HltbEntry? Lookup(string gameTitle, string? launchboxPlatform = null)
    {
        if (string.IsNullOrEmpty(gameTitle)) return null;

        var normalized = NameMatcher.Normalize(gameTitle);
        var stripped = NameMatcher.StripEditionSuffix(normalized);
        var hltbPlatform = PlatformMappings.Map(launchboxPlatform ?? "");

        HltbLogger.Debug($"Lookup: '{gameTitle}' [{launchboxPlatform}] -> norm='{normalized}', stripped='{stripped}', hltb_platform='{hltbPlatform}'");

        // Stage 0: Known alias override (use stripped form for alias lookup)
        var aliasKey = stripped == normalized ? normalized : stripped;
        var canonical = KnownAliases.TryGetCanonical(aliasKey);
        if (canonical != null)
        {
            var canonicalNorm = NameMatcher.Normalize(canonical);
            if (_nameIndex.TryGetValue(canonicalNorm, out var aliasCandidates))
            {
                var aliasMatch = PreferPlatform(aliasCandidates, hltbPlatform);
                if (aliasMatch != null)
                {
                    HltbLogger.Info($"Match via KnownAlias: '{gameTitle}' -> '{aliasMatch.Name}' (alias='{canonical}')");
                    return aliasMatch;
                }
            }
            HltbLogger.Debug($"KnownAlias found '{canonical}' but no matching entry in index");
        }

        // Stage 1: Exact normalized name match (primary index) - prefers full title match
        if (_nameIndex.TryGetValue(normalized, out var candidates))
        {
            var match = PreferPlatform(candidates, hltbPlatform);
            if (match != null)
            {
                HltbLogger.Info($"Match via NormalizedName: '{gameTitle}' -> '{match.Name}' [{match.Platform}]");
                return match;
            }
        }

        // Stage 2: Suffix-stripped form against primary index (e.g. user queries "FF7 Remake" -> "ff7", finds FF7)
        if (stripped != normalized && _nameIndex.TryGetValue(stripped, out var strippedCandidates))
        {
            var match = PreferPlatform(strippedCandidates, hltbPlatform);
            if (match != null)
            {
                HltbLogger.Info($"Match via SuffixStripped: '{gameTitle}' -> '{match.Name}' [{match.Platform}]");
                return match;
            }
        }

        // Stage 3: Variant index with original normalized form
        if (_variantIndex.TryGetValue(normalized, out var variantCandidates))
        {
            var match = PreferPlatform(variantCandidates, hltbPlatform);
            if (match != null)
            {
                HltbLogger.Info($"Match via VariantIndex: '{gameTitle}' -> '{match.Name}' [{match.Platform}]");
                return match;
            }
        }

        // Stage 4: Variant index with suffix-stripped form
        if (stripped != normalized && _variantIndex.TryGetValue(stripped, out var variantStripped))
        {
            var match = PreferPlatform(variantStripped, hltbPlatform);
            if (match != null)
            {
                HltbLogger.Info($"Match via VariantIndex+Stripped: '{gameTitle}' -> '{match.Name}' [{match.Platform}]");
                return match;
            }
        }

        // Stage 5: Generate variants of the query and try matching
        var queryVariants = NameMatcher.GenerateVariants(gameTitle);
        foreach (var variant in queryVariants)
        {
            if (variant == normalized || variant == stripped) continue;
            if (_nameIndex.TryGetValue(variant, out var vc))
            {
                var match = PreferPlatform(vc, hltbPlatform);
                if (match != null)
                {
                    HltbLogger.Info($"Match via QueryVariant '{variant}': '{gameTitle}' -> '{match.Name}'");
                    return match;
                }
            }
            if (_variantIndex.TryGetValue(variant, out var vc2))
            {
                var match = PreferPlatform(vc2, hltbPlatform);
                if (match != null)
                {
                    HltbLogger.Info($"Match via QueryVariant+VariantIndex '{variant}': '{gameTitle}' -> '{match.Name}'");
                    return match;
                }
            }
        }

        // Stage 6: Fuzzy match (Levenshtein <= 3 on stripped form)
        HltbEntry? fuzzyBest = null;
        var bestFuzzyScore = int.MaxValue;
        var fuzzyTarget = stripped;

        foreach (var kvp in _nameIndex)
        {
            // Skip if length too different (saves time on huge dataset)
            if (Math.Abs(kvp.Key.Length - fuzzyTarget.Length) > 3) continue;

            var distance = LevenshteinDistance(fuzzyTarget, kvp.Key);
            if (distance < bestFuzzyScore && distance <= 3)
            {
                var withData = PreferPlatform(kvp.Value, hltbPlatform);
                if (withData != null)
                {
                    bestFuzzyScore = distance;
                    fuzzyBest = withData;
                    if (distance == 0) break;
                }
            }
        }

        if (fuzzyBest != null)
        {
            HltbLogger.Info($"Match via Fuzzy(dist={bestFuzzyScore}): '{gameTitle}' -> '{fuzzyBest.Name}'");
            return fuzzyBest;
        }

        HltbLogger.Warn($"No match: '{gameTitle}' [{launchboxPlatform}]");
        return null;
    }

    /// <summary>
    /// From a list of candidates, prefer one matching the platform.
    /// Among matches, rank by popularity (poll count) so canonical entries beat obscure ones.
    /// </summary>
    private static HltbEntry? PreferPlatform(List<HltbEntry> candidates, string hltbPlatform)
    {
        if (candidates.Count == 0) return null;

        // Only consider entries with actual data
        var withData = candidates.Where(c => c.HasAnyData && IsGameType(c)).ToList();
        if (withData.Count == 0) return null;

        if (!string.IsNullOrEmpty(hltbPlatform))
        {
            var platformMatches = withData
                .Where(c => c.PlatformList.Any(p => string.Equals(p, hltbPlatform, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(c => c.MainStoryPolled + c.MainPlusSidesPolled + c.CompletionistPolled)
                .ToList();
            if (platformMatches.Count > 0) return platformMatches[0];
        }

        // No platform match - return most popular
        return withData
            .OrderByDescending(c => c.MainStoryPolled + c.MainPlusSidesPolled + c.CompletionistPolled)
            .First();
    }

    /// <summary>Exclude DLCs, expansions, omitted entries.</summary>
    private static bool IsGameType(HltbEntry e)
    {
        var t = e.Type?.ToLowerInvariant() ?? "";
        return t == "game" || t == "";
    }

    /// <summary>Search for matching HLTB entries by name (returns top N matches).</summary>
    public List<HltbEntry> Search(string query, int maxResults = 20)
    {
        if (string.IsNullOrEmpty(query)) return new List<HltbEntry>();

        var normalized = NameMatcher.Normalize(query);
        var stripped = NameMatcher.StripEditionSuffix(normalized);
        var results = new Dictionary<string, (HltbEntry Entry, int Distance)>();

        // Add exact matches
        foreach (var key in new[] { normalized, stripped }.Distinct())
        {
            if (_nameIndex.TryGetValue(key, out var exact))
            {
                foreach (var e in exact.Where(e => e.HasAnyData))
                    if (!results.ContainsKey(e.Id)) results[e.Id] = (e, 0);
            }
            if (_variantIndex.TryGetValue(key, out var exactVariants))
            {
                foreach (var e in exactVariants.Where(e => e.HasAnyData))
                    if (!results.ContainsKey(e.Id)) results[e.Id] = (e, 0);
            }
        }

        // Fuzzy search
        foreach (var kvp in _nameIndex)
        {
            if (Math.Abs(kvp.Key.Length - stripped.Length) > 3) continue;
            var distance = LevenshteinDistance(stripped, kvp.Key);
            if (distance <= 3)
            {
                foreach (var e in kvp.Value.Where(e => e.HasAnyData))
                {
                    if (!results.ContainsKey(e.Id) || results[e.Id].Distance > distance)
                        results[e.Id] = (e, distance);
                }
            }
        }

        return results.Values
            .OrderBy(r => r.Distance)
            .ThenByDescending(r => r.Entry.MainStoryPolled + r.Entry.MainPlusSidesPolled + r.Entry.CompletionistPolled)
            .Take(maxResults)
            .Select(r => r.Entry)
            .ToList();
    }

    private static int LevenshteinDistance(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b)) return a.Length;

        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        for (int j = 1; j <= b.Length; j++)
        {
            var cost = a[i - 1] == b[j - 1] ? 0 : 1;
            d[i, j] = Math.Min(Math.Min(
                d[i - 1, j] + 1,
                d[i, j - 1] + 1),
                d[i - 1, j - 1] + cost);
        }

        return d[a.Length, b.Length];
    }
}
