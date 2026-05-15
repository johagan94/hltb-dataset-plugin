using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using Unbroken.LaunchBox.Plugins.Data;

namespace HltbDatasetPlugin;

public static partial class Etc
{
    /// <summary>Format a playtime in hours to the configured display format.</summary>
    public static string FormatTime(double hours, TimeFormat format, int polled = 0, bool showPolled = false)
    {
        if (hours <= 0) return "";

        var result = format switch
        {
            TimeFormat.HoursMinutes => HoursToHm(hours),
            TimeFormat.HoursOnly => $"{Math.Round(hours)}h",
            TimeFormat.MinutesOnly => $"{Math.Round(hours * 60)}m",
            TimeFormat.Decimal => $"{hours:F1}h",
            _ => HoursToHm(hours)
        };

        if (showPolled && polled > 0)
            result += $" ({polled} polled)";

        return result;
    }

    /// <summary>Convert hours to "Xh Ym" format.</summary>
    public static string HoursToHm(double hours)
    {
        if (hours <= 0) return "";
        var h = (int)hours;
        var m = (int)Math.Round((hours - h) * 60);
        if (m >= 60) { h += 1; m -= 60; }

        return m > 0 ? $"{h}h {m}m" : $"{h}h";
    }

    /// <summary>Format time for sorting (minutes total as string, zero-padded).</summary>
    public static int TimeToMinutes(string formatted)
    {
        if (string.IsNullOrEmpty(formatted)) return 0;
        var match = TimeSortRegex().Match(formatted);
        if (!match.Success) return 0;

        var hours = int.TryParse(match.Groups[1].Value, out var h) ? h : 0;
        var minutes = int.TryParse(match.Groups[2].Value, out var m) ? m : 0;
        return hours * 60 + minutes;
    }

    // ===== Custom Field Helpers =====

    public static bool HasCustomField(IGame game, string name)
    {
        return game.GetAllCustomFields().Any(f => f.Name == name);
    }

    public static void SetCustomField(IGame game, string name, string value)
    {
        var existing = game.GetAllCustomFields().FirstOrDefault(f => f.Name == name);
        if (existing != null)
            game.TryRemoveCustomField(existing);

        var field = game.AddNewCustomField();
        field.Name = name;
        field.Value = value;
    }

    public static void RemoveCustomField(IGame game, string name)
    {
        var existing = game.GetAllCustomFields().FirstOrDefault(f => f.Name == name);
        if (existing != null)
            game.TryRemoveCustomField(existing);
    }

    public const string FieldMainStory = "HLTB Main Story";
    public const string FieldMainPlusExtras = "HLTB Main + Extras";
    public const string FieldCompletionist = "HLTB Completionist";

    /// <summary>
    /// Execute an action on the UI thread. Tries WPF Dispatcher first (LaunchBox primary),
    /// falls back to WinForms SynchronizationContext, then executes directly as last resort.
    /// </summary>
    public static void InvokeOnUi(Action action)
    {
        if (action == null) return;

        // Try WPF Dispatcher (LaunchBox is WPF)
        try
        {
            var app = System.Windows.Application.Current;
            if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.Invoke(action);
                return;
            }
            else if (app?.Dispatcher != null)
            {
                action();
                return;
            }
        }
        catch { }

        // Fallback: WinForms SynchronizationContext
        try
        {
            var ctx = System.Threading.SynchronizationContext.Current;
            if (ctx != null)
            {
                ctx.Send(_ => action(), null);
                return;
            }
        }
        catch { }

        // Last resort: execute directly (may cause cross-thread issues but better than nothing)
        action();
    }

    [GeneratedRegex(@"(\d+)h\s*(\d+)?m?")]
    private static partial Regex TimeSortRegex();
}
