using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace HltbDatasetPlugin;

/// <summary>
/// Live HLTB scraper used when a game isn't in the local dataset.
/// Hits the public POST /api/search endpoint. Caches results in memory + on disk.
/// </summary>
public class HltbScraper
{
    private const string SearchUrl = "https://howlongtobeat.com/api/search";
    private const string FinderInitUrl = "https://howlongtobeat.com/api/finder/init";
    private const string FinderUrl = "https://howlongtobeat.com/api/finder";
    private const string BaseUrl = "https://howlongtobeat.com";
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; LaunchBox HLTB Plugin)";

    private static readonly HttpClient _http = CreateClient();
    private readonly Dictionary<string, HltbEntry?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheLock = new();
    private readonly SemaphoreSlim _rateLimit = new(1, 1);
    private DateTime _lastRequest = DateTime.MinValue;
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(800);

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip
                                   | System.Net.DecompressionMethods.Deflate,
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        return client;
    }

    public async Task<HltbEntry?> SearchAsync(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var cacheKey = NameMatcher.Normalize(title);
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                HltbLogger.Debug($"Scrape cache HIT: '{title}' -> {(cached != null ? cached.Name : "null")}");
                return cached;
            }
        }

        HltbLogger.Info($"Scrape: searching HLTB for '{title}'...");

        await ThrottleAsync();

        // Try legacy /api/search first (no token needed)
        var result = await TryLegacyApiAsync(title);

        // Fallback: try finder API with token
        if (result == null)
        {
            HltbLogger.Debug("Legacy search returned no results. Trying finder API...");
            result = await TryFinderApiAsync(title);
        }

        lock (_cacheLock)
        {
            _cache[cacheKey] = result;
        }

        if (result != null)
            HltbLogger.Info($"Scrape OK: '{title}' -> '{result.Name}' (id={result.Id}, main={result.MainStory}h)");
        else
            HltbLogger.Warn($"Scrape FAIL: '{title}' - no results from HLTB");

        return result;
    }

    private async Task ThrottleAsync()
    {
        await _rateLimit.WaitAsync();
        try
        {
            var since = DateTime.UtcNow - _lastRequest;
            if (since < MinRequestInterval)
            {
                var delay = MinRequestInterval - since;
                await Task.Delay(delay);
            }
            _lastRequest = DateTime.UtcNow;
        }
        finally
        {
            _rateLimit.Release();
        }
    }

    /// <summary>Try the legacy /api/search endpoint (no token required as of 2026).</summary>
    private async Task<HltbEntry?> TryLegacyApiAsync(string title)
    {
        try
        {
            var searchTerms = title.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var body = new SearchRequest
            {
                SearchType = "games",
                SearchTerms = searchTerms,
                SearchPage = 1,
                Size = 20,
                SearchOptions = new SearchOptions
                {
                    Games = new SearchGames
                    {
                        UserId = 0,
                        Platform = "",
                        SortCategory = "popular",
                        RangeCategory = "main",
                        RangeTime = new RangeTime { Min = 0, Max = 0 },
                        Gameplay = new Gameplay { Perspective = "", Flow = "", Genre = "" },
                        Modifier = ""
                    },
                    Users = new SearchUsers { SortCategory = "postcount" },
                    Filter = "",
                    Sort = 0,
                    Randomizer = 0
                }
            };

            var json = JsonSerializer.Serialize(body, JsonOpts);
            using var req = new HttpRequestMessage(HttpMethod.Post, SearchUrl);
            req.Headers.Referrer = new Uri(BaseUrl + "/");
            req.Headers.TryAddWithoutValidation("Origin", BaseUrl);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                HltbLogger.Debug($"Legacy /api/search returned {(int)resp.StatusCode} {resp.StatusCode}");
                return null;
            }

            var text = await resp.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SearchResponse>(text, JsonOpts);
            if (result?.Data == null || result.Data.Count == 0)
                return null;

            return PickBest(title, result.Data);
        }
        catch (Exception ex)
        {
            HltbLogger.Debug($"Legacy /api/search error: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Try the newer /api/finder endpoint which uses a token from /api/finder/init.</summary>
    private async Task<HltbEntry?> TryFinderApiAsync(string title)
    {
        try
        {
            // Step 1: Get token
            using var initReq = new HttpRequestMessage(HttpMethod.Post, FinderInitUrl);
            initReq.Headers.Referrer = new Uri(BaseUrl + "/");
            initReq.Headers.TryAddWithoutValidation("Origin", BaseUrl);
            initReq.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            using var initResp = await _http.SendAsync(initReq);
            if (!initResp.IsSuccessStatusCode)
            {
                HltbLogger.Debug($"Finder init returned {(int)initResp.StatusCode}");
                return null;
            }

            var initJson = await initResp.Content.ReadAsStringAsync();
            using var initDoc = JsonDocument.Parse(initJson);
            string? token = null;
            if (initDoc.RootElement.TryGetProperty("token", out var tokenEl))
                token = tokenEl.GetString();

            if (string.IsNullOrEmpty(token))
            {
                HltbLogger.Debug("Finder init returned no token");
                return null;
            }

            await ThrottleAsync();

            // Step 2: Search
            var searchTerms = title.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var body = new SearchRequest
            {
                SearchType = "games",
                SearchTerms = searchTerms,
                SearchPage = 1,
                Size = 20,
                SearchOptions = new SearchOptions
                {
                    Games = new SearchGames
                    {
                        UserId = 0,
                        Platform = "",
                        SortCategory = "popular",
                        RangeCategory = "main",
                        RangeTime = new RangeTime { Min = 0, Max = 0 },
                        Gameplay = new Gameplay { Perspective = "", Flow = "", Genre = "" },
                        Modifier = ""
                    },
                    Users = new SearchUsers { SortCategory = "postcount" },
                    Filter = "",
                    Sort = 0,
                    Randomizer = 0
                }
            };

            var json = JsonSerializer.Serialize(body, JsonOpts);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{FinderUrl}?token={token}");
            req.Headers.Referrer = new Uri(BaseUrl + "/");
            req.Headers.TryAddWithoutValidation("Origin", BaseUrl);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                HltbLogger.Debug($"Finder search returned {(int)resp.StatusCode}");
                return null;
            }

            var text = await resp.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SearchResponse>(text, JsonOpts);
            if (result?.Data == null || result.Data.Count == 0)
                return null;

            return PickBest(title, result.Data);
        }
        catch (Exception ex)
        {
            HltbLogger.Debug($"Finder API error: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static HltbEntry? PickBest(string query, List<HltbSearchResult> data)
    {
        var queryNorm = NameMatcher.Normalize(query);
        HltbSearchResult? best = null;
        var bestScore = double.MaxValue;

        foreach (var r in data)
        {
            if (string.IsNullOrEmpty(r.GameName)) continue;
            var entryNorm = NameMatcher.Normalize(r.GameName);
            var distance = Levenshtein(queryNorm, entryNorm);
            var maxLen = Math.Max(queryNorm.Length, entryNorm.Length);
            if (maxLen == 0) continue;

            var relativeScore = (double)distance / maxLen;
            if (relativeScore < bestScore)
            {
                bestScore = relativeScore;
                best = r;
            }
        }

        if (best == null || bestScore > 0.5)
        {
            HltbLogger.Debug($"PickBest: no result close enough. Best score: {bestScore}");
            return null;
        }

        // Convert from HLTB seconds to hours
        return new HltbEntry
        {
            Id = best.GameId.ToString(),
            Name = best.GameName ?? "",
            Platform = "",
            MainStory = best.CompMain / 3600.0,
            MainStoryPolled = best.CompMainCount,
            MainPlusSides = best.CompPlus / 3600.0,
            MainPlusSidesPolled = best.CompPlusCount,
            Completionist = best.Comp100 / 3600.0,
            CompletionistPolled = best.Comp100Count,
            AllStyles = best.CompAll / 3600.0,
            AllStylesPolled = best.CompAllCount,
            SourceUrl = $"{BaseUrl}/game/{best.GameId}",
            NormalizedName = NameMatcher.Normalize(best.GameName ?? "")
        };
    }

    private static int Levenshtein(string a, string b)
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

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ===== JSON Models =====
    private class SearchRequest
    {
        [JsonPropertyName("searchType")] public string SearchType { get; set; } = "";
        [JsonPropertyName("searchTerms")] public string[] SearchTerms { get; set; } = Array.Empty<string>();
        [JsonPropertyName("searchPage")] public int SearchPage { get; set; }
        [JsonPropertyName("size")] public int Size { get; set; }
        [JsonPropertyName("searchOptions")] public SearchOptions SearchOptions { get; set; } = new();
    }

    private class SearchOptions
    {
        [JsonPropertyName("games")] public SearchGames Games { get; set; } = new();
        [JsonPropertyName("users")] public SearchUsers Users { get; set; } = new();
        [JsonPropertyName("filter")] public string Filter { get; set; } = "";
        [JsonPropertyName("sort")] public int Sort { get; set; }
        [JsonPropertyName("randomizer")] public int Randomizer { get; set; }
    }

    private class SearchGames
    {
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("platform")] public string Platform { get; set; } = "";
        [JsonPropertyName("sortCategory")] public string SortCategory { get; set; } = "";
        [JsonPropertyName("rangeCategory")] public string RangeCategory { get; set; } = "";
        [JsonPropertyName("rangeTime")] public RangeTime RangeTime { get; set; } = new();
        [JsonPropertyName("gameplay")] public Gameplay Gameplay { get; set; } = new();
        [JsonPropertyName("modifier")] public string Modifier { get; set; } = "";
    }

    private class RangeTime
    {
        [JsonPropertyName("min")] public int Min { get; set; }
        [JsonPropertyName("max")] public int Max { get; set; }
    }

    private class Gameplay
    {
        [JsonPropertyName("perspective")] public string Perspective { get; set; } = "";
        [JsonPropertyName("flow")] public string Flow { get; set; } = "";
        [JsonPropertyName("genre")] public string Genre { get; set; } = "";
    }

    private class SearchUsers
    {
        [JsonPropertyName("sortCategory")] public string SortCategory { get; set; } = "";
    }

    private class SearchResponse
    {
        [JsonPropertyName("count")] public int Count { get; set; }
        [JsonPropertyName("data")] public List<HltbSearchResult>? Data { get; set; }
    }

    private class HltbSearchResult
    {
        [JsonPropertyName("game_id")] public int GameId { get; set; }
        [JsonPropertyName("game_name")] public string? GameName { get; set; }
        [JsonPropertyName("game_type")] public string? GameType { get; set; }
        [JsonPropertyName("game_image")] public string? GameImage { get; set; }
        [JsonPropertyName("comp_main")] public int CompMain { get; set; }
        [JsonPropertyName("comp_main_count")] public int CompMainCount { get; set; }
        [JsonPropertyName("comp_plus")] public int CompPlus { get; set; }
        [JsonPropertyName("comp_plus_count")] public int CompPlusCount { get; set; }
        [JsonPropertyName("comp_100")] public int Comp100 { get; set; }
        [JsonPropertyName("comp_100_count")] public int Comp100Count { get; set; }
        [JsonPropertyName("comp_all")] public int CompAll { get; set; }
        [JsonPropertyName("comp_all_count")] public int CompAllCount { get; set; }
    }
}
