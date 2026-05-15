using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Linq;
using Unbroken.LaunchBox.Plugins.Data;

namespace HltbDatasetPlugin;

public class HltbBadge : IGameBadge
{
    private const int MaxCacheSize = 10_000;
    private const int EvictTarget = MaxCacheSize / 2;

    private static Image? _icon;
    private static readonly ConcurrentDictionary<string, bool> _cache = new();
    private static long _callCount;
    private static long _hitCount;

    public string UniqueId => "HltbDatasetBadge";
    public string Name => "HLTB Time Available";
    public int Index { get; set; } = 100;
    public Image DefaultIcon => _icon ??= CreateBadgeIcon();

    public bool GetAppliesToGame(IGame game)
    {
        if (game == null) return false;
        var calls = System.Threading.Interlocked.Increment(ref _callCount);

        // Log first call and every 1000th call to track UI thread pressure
        if (calls == 1)
            HltbLogger.Debug($"Badge: first GetAppliesToGame call");
        else if (calls % 1000 == 0)
            HltbLogger.Debug($"Badge: GetAppliesToGame call #{calls}, hits={_hitCount}, cache_size={_cache.Count}");

        try
        {
            var id = game.Id;
            if (string.IsNullOrEmpty(id)) return false;

            // Evict old entries if cache is too large (prevent unbounded memory growth)
            if (_cache.Count > MaxCacheSize)
                EvictOldest();

            // Cache by game ID. Cache is invalidated implicitly when plugin reloads.
            return _cache.GetOrAdd(id, _ =>
            {
                var has = game.GetAllCustomFields().Any(f =>
                    f.Name == Etc.FieldMainStory || f.Name == Etc.FieldMainPlusExtras);
                if (has) System.Threading.Interlocked.Increment(ref _hitCount);
                return has;
            });
        }
        catch (Exception ex)
        {
            HltbLogger.Error($"Badge GetAppliesToGame error for '{game.Title}'", ex);
            return false;
        }
    }

    /// <summary>Remove oldest entries when cache exceeds MaxCacheSize.</summary>
    private static void EvictOldest()
    {
        // Simple eviction: remove entries until we're under the target
        var toRemove = _cache.Count - EvictTarget;
        if (toRemove <= 0) return;

        var removed = 0;
        foreach (var kvp in _cache)
        {
            if (_cache.TryRemove(kvp.Key, out _))
            {
                removed++;
                if (removed >= toRemove) break;
            }
        }
        HltbLogger.Debug($"Badge: evicted {removed} entries (cache was {_cache.Count + removed}, now {_cache.Count})");
    }

    /// <summary>Invalidate cache entry for a game (call after adding/removing HLTB custom fields).</summary>
    public static void InvalidateCache(string gameId)
    {
        if (!string.IsNullOrEmpty(gameId))
            _cache.TryRemove(gameId, out _);
    }

    /// <summary>Clear all cached badge state.</summary>
    public static void ClearCache()
    {
        _cache.Clear();
    }

    private static Image CreateBadgeIcon()
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var brush = new SolidBrush(Color.FromArgb(70, 130, 180));
        g.FillEllipse(brush, 1, 1, 14, 14);

        using var pen = new Pen(Color.White, 2);
        pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
        pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;

        g.DrawLine(pen, 8, 8, 8, 5);
        g.DrawLine(pen, 8, 8, 11, 8);

        return bmp;
    }
}
