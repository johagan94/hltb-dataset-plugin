using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HltbDatasetPlugin;

/// <summary>
/// Handles name normalization and variant generation for fuzzy matching.
/// Critical for dual-release titles (e.g. "Pokemon Blue" = "Pokemon Red and Blue").
/// </summary>
public static partial class NameMatcher
{
    private static readonly string[] DualSeparators = { " and ", " & ", " / ", "/" };

    // Order matters: longer suffixes first so "Definitive Edition" is matched before "Edition"
    private static readonly string[] VersionSuffixes =
    {
        " definitive edition", " ultimate edition", " complete edition",
        " enhanced edition", " anniversary edition", " collectors edition",
        " collector's edition", " special edition", " gold edition", " platinum edition",
        " digital edition", " day one edition", " limited edition", " hd edition",
        " game of the year", " director's cut", " directors cut",
        " version", " edition", " remake", " remastered", " remaster",
        " goty", " hd", " plus", " ex", " dx"
    };

    // ROM region/group tags commonly appended to filenames
    private static readonly Regex RomTagRegex = new(@"\s*\((?:USA|Europe|Japan|World|En|Eu|Jp|Fr|De|Es|It|Rev\s*[0-9A-Z]+|v[0-9.]+|Beta|Proto|Demo|Unl|!|Disc\s*[0-9]+|Disk\s*[0-9]+|N64|GBA|GBC|SNES|NES|U|J|E|UE|JU)\)\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // [b], [t1], [!] etc. trailing brackets
    private static readonly Regex RomBracketRegex = new(@"\s*\[[^\]]*\]\s*", RegexOptions.Compiled);

    /// <summary>Strip Unicode diacritics: é→e, ñ→n, ō→o, etc.</summary>
    public static string StripDiacritics(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Normalize a name for matching: lowercase, strip diacritics + punctuation, collapse spaces,
    /// remove leading articles, strip edition suffixes.
    /// </summary>
    /// <summary>
    /// Normalize a name for matching. Strips diacritics, ROM tags, punctuation, articles.
    /// Does NOT strip edition suffixes - use StripEditionSuffix separately so we can keep both forms indexed.
    /// </summary>
    public static string Normalize(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";

        var n = StripDiacritics(name);

        // Strip ROM region/group tags
        n = RomTagRegex.Replace(n, " ");
        n = RomBracketRegex.Replace(n, " ");

        n = n.ToLowerInvariant();

        // Strip non-alphanumeric (keep spaces)
        n = NonAlphaRegex().Replace(n, " ");
        n = CollapseSpacesRegex().Replace(n, " ").Trim();

        // Remove leading article
        if (n.StartsWith("the ")) n = n[4..];
        if (n.StartsWith("a ")) n = n[2..];

        return n;
    }

    /// <summary>
    /// Strip edition suffixes from an already-normalized name.
    /// E.g. "final fantasy vii remake" -> "final fantasy vii"
    /// E.g. "skyrim definitive edition" -> "skyrim"
    /// </summary>
    public static string StripEditionSuffix(string normalized)
    {
        if (string.IsNullOrEmpty(normalized)) return normalized;
        var n = normalized;

        bool changed;
        do
        {
            changed = false;
            foreach (var suffix in VersionSuffixes)
            {
                if (n.EndsWith(suffix))
                {
                    n = n[..^suffix.Length].TrimEnd(' ', '-', ':');
                    changed = true;
                    break;
                }
            }
        } while (changed);

        return CollapseSpacesRegex().Replace(n, " ").Trim();
    }

    /// <summary>
    /// Generate possible matching variants for a game title.
    /// E.g. "Pokemon Blue" → ["pokemon blue", "pokemon red blue", "pokemon blue red"]
    /// E.g. "Pokemon Red and Blue" → ["pokemon red and blue", "pokemon red", "pokemon blue", "pokemon red blue", "pokemon blue red"]
    /// </summary>
    public static List<string> GenerateVariants(string name)
    {
        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = Normalize(name);
        if (string.IsNullOrEmpty(normalized)) return new List<string>();

        variants.Add(normalized);

        // Subtitle splits: "007: Quantum of Solace" -> "quantum of solace", "Halo: Combat Evolved" -> "halo", "combat evolved"
        var subtitleParts = SplitOnSubtitleSeparator(name);
        if (subtitleParts.Count > 1)
        {
            foreach (var part in subtitleParts)
            {
                var partNorm = Normalize(part);
                if (!string.IsNullOrEmpty(partNorm) && partNorm.Length >= 4)
                    variants.Add(partNorm);
            }
        }

        // Split on dual-release separators
        var parts = SplitOnDualSeparators(name);
        if (parts.Count > 1)
        {
            // Add each part as a possible match
            // For "Pokemon Red and Blue", parts = ["Pokemon Red", "Blue"]
            // We need to extract the prefix that's common
            var prefix = ExtractCommonPrefix(parts);
            foreach (var part in parts)
            {
                var normalizedPart = Normalize(part);
                if (!string.IsNullOrEmpty(normalizedPart))
                    variants.Add(normalizedPart);

                // Combine with prefix if not already there
                if (!string.IsNullOrEmpty(prefix) && !part.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var combined = Normalize($"{prefix} {part}");
                    if (!string.IsNullOrEmpty(combined))
                        variants.Add(combined);
                }
            }

            // Also try with " and " variations (e.g. "red blue" with all separator forms)
            // already covered by normalize stripping " and " punctuation
        }

        return variants.ToList();
    }

    /// <summary>
    /// Generate "reverse" lookup keys: for an HLTB entry "Pokemon Red and Blue",
    /// generate keys ["pokemon red and blue", "pokemon red", "pokemon blue"].
    /// For "The Elder Scrolls V: Skyrim" generates ["the elder scrolls v skyrim", "skyrim", "elder scrolls v"].
    /// For "Pokemon Yellow: Special Pikachu Edition" generates ["pokemon yellow special pikachu", "pokemon yellow"].
    /// This lets us look up partial titles and find canonical entries.
    /// </summary>
    public static List<string> GenerateLookupKeys(string entryName)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = Normalize(entryName);
        if (string.IsNullOrEmpty(normalized)) return new List<string>();

        keys.Add(normalized);

        // ===== Subtitle splits ":" or " - " =====
        // E.g. "The Elder Scrolls V: Skyrim" -> generate "the elder scrolls v" and "skyrim"
        // E.g. "Pokemon Yellow: Special Pikachu Edition" -> "pokemon yellow"
        // Use 4+ char minimum to avoid Roman numeral / abbreviation collisions ("V", "VII")
        var colonSplit = SplitOnSubtitleSeparator(entryName);
        if (colonSplit.Count > 1)
        {
            foreach (var part in colonSplit)
            {
                var partNorm = Normalize(part);
                if (!string.IsNullOrEmpty(partNorm) && partNorm.Length >= 4)
                    keys.Add(partNorm);
            }
        }

        // ===== Dual-release splits (and / & / /) =====
        var parts = SplitOnDualSeparators(entryName);
        if (parts.Count > 1)
        {
            var prefix = ExtractCommonPrefix(parts);

            foreach (var part in parts)
            {
                var bare = part.Trim();
                if (!string.IsNullOrEmpty(prefix) && bare.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    bare = bare[prefix.Length..].Trim();
                }

                if (!string.IsNullOrEmpty(prefix))
                {
                    var combined = Normalize($"{prefix} {bare}");
                    if (!string.IsNullOrEmpty(combined))
                        keys.Add(combined);
                }

                var partNorm = Normalize(bare);
                if (!string.IsNullOrEmpty(partNorm))
                    keys.Add(partNorm);
            }
        }

        return keys.ToList();
    }

    /// <summary>Split on ":" or " - " separator. Only returns multiple parts if separator found.</summary>
    public static List<string> SplitOnSubtitleSeparator(string name)
    {
        if (name.Contains(':'))
            return name.Split(':').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        if (name.Contains(" - "))
            return name.Split(new[] { " - " }, StringSplitOptions.None).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        return new List<string> { name };
    }

    /// <summary>Split a name on dual-release separators like " and ", " & ", " / ", "/".</summary>
    public static List<string> SplitOnDualSeparators(string name)
    {
        var parts = new List<string> { name };
        foreach (var sep in DualSeparators)
        {
            var newParts = new List<string>();
            foreach (var p in parts)
            {
                if (p.Contains(sep, StringComparison.OrdinalIgnoreCase))
                {
                    newParts.AddRange(p.Split(new[] { sep }, StringSplitOptions.None));
                }
                else
                {
                    newParts.Add(p);
                }
            }
            parts = newParts;
        }
        return parts.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
    }

    /// <summary>
    /// Extract the common word prefix from a list of strings.
    /// E.g. ["Pokemon Red", "Blue"] → "" (no common prefix on the right side)
    /// E.g. ["Pokemon Red", "Pokemon Blue"] → "Pokemon"
    /// For dual-release titles, this often returns the franchise name.
    /// </summary>
    public static string ExtractCommonPrefix(List<string> parts)
    {
        if (parts.Count < 2) return "";

        // Special case: "Pokemon Red and Blue" splits to ["Pokemon Red", "Blue"]
        // The prefix is just the franchise word(s) of the first part
        // Heuristic: take all words except the last from the first part
        var firstWords = parts[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (firstWords.Length >= 2)
        {
            // Return all words except the last
            return string.Join(" ", firstWords.Take(firstWords.Length - 1));
        }
        return "";
    }

    [GeneratedRegex(@"[^a-z0-9\s]")]
    private static partial Regex NonAlphaRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex CollapseSpacesRegex();
}
