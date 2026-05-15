using System;
using System.Diagnostics;
using HltbDatasetPlugin;

namespace HltbDatasetPlugin.Tests;

public class Program
{
    private static int _pass;
    private static int _fail;

    public static int Main(string[] args)
    {
        HltbLogger.Enabled = false; // quiet tests

        Console.WriteLine("=== NameMatcher Tests ===");
        TestNormalize();
        TestStripDiacritics();
        TestSplitOnDualSeparators();
        TestGenerateLookupKeys();
        TestGenerateVariants();
        TestKnownAliases();

        Console.WriteLine();
        Console.WriteLine("=== Dataset Tests ===");
        var datasetDir = args.Length > 0 ? args[0] : @"C:\Users\Jack\Documents\HLTB Dataset";
        TestDatasetLookups(datasetDir);

        Console.WriteLine();
        Console.WriteLine($"=== Results: {_pass} passed, {_fail} failed ===");
        return _fail == 0 ? 0 : 1;
    }

    private static void TestNormalize()
    {
        Assert("Normalize: lowercase + strip punctuation",
            NameMatcher.Normalize("Final Fantasy VII"), "final fantasy vii");

        Assert("Normalize: strip diacritics",
            NameMatcher.Normalize("Pokémon Red"), "pokemon red");

        // Normalize no longer strips edition suffixes (that's StripEditionSuffix's job)
        Assert("Normalize: keeps Remake suffix",
            NameMatcher.Normalize("Final Fantasy VII Remake"), "final fantasy vii remake");

        Assert("StripEditionSuffix: Remake",
            NameMatcher.StripEditionSuffix(NameMatcher.Normalize("Final Fantasy VII Remake")), "final fantasy vii");

        Assert("StripEditionSuffix: Version",
            NameMatcher.StripEditionSuffix(NameMatcher.Normalize("Pokemon Blue Version")), "pokemon blue");

        Assert("StripEditionSuffix: Definitive Edition",
            NameMatcher.StripEditionSuffix(NameMatcher.Normalize("Skyrim Definitive Edition")), "skyrim");

        Assert("Normalize: leading article",
            NameMatcher.Normalize("The Legend of Zelda"), "legend of zelda");

        Assert("Normalize: collapse spaces",
            NameMatcher.Normalize("  Multi   Space   Game  "), "multi space game");

        Assert("Normalize: special chars to space",
            NameMatcher.Normalize("Half-Life 2: Episode One"), "half life 2 episode one");

        Assert("Normalize: empty string",
            NameMatcher.Normalize(""), "");

        Assert("Normalize: ampersand",
            NameMatcher.Normalize("Banjo & Kazooie"), "banjo kazooie");
    }

    private static void TestStripDiacritics()
    {
        Assert("StripDiacritics: é -> e",
            NameMatcher.StripDiacritics("Pokémon"), "Pokemon");

        Assert("StripDiacritics: ñ -> n",
            NameMatcher.StripDiacritics("España"), "Espana");

        Assert("StripDiacritics: ō -> o",
            NameMatcher.StripDiacritics("Tōkyō"), "Tokyo");

        Assert("StripDiacritics: no change",
            NameMatcher.StripDiacritics("Hello World"), "Hello World");
    }

    private static void TestSplitOnDualSeparators()
    {
        var r1 = NameMatcher.SplitOnDualSeparators("Pokemon Red and Blue");
        AssertList("Split: 'X and Y'", r1, new[] { "Pokemon Red", "Blue" });

        var r2 = NameMatcher.SplitOnDualSeparators("Pokemon Mystery Dungeon: Blue/Red Rescue Team");
        AssertList("Split: 'X/Y'", r2, new[] { "Pokemon Mystery Dungeon: Blue", "Red Rescue Team" });

        var r3 = NameMatcher.SplitOnDualSeparators("Banjo & Kazooie");
        AssertList("Split: 'X & Y'", r3, new[] { "Banjo", "Kazooie" });

        var r4 = NameMatcher.SplitOnDualSeparators("Single Title");
        AssertList("Split: no separator", r4, new[] { "Single Title" });
    }

    private static void TestGenerateLookupKeys()
    {
        var keys = NameMatcher.GenerateLookupKeys("Pokemon Red and Blue");
        AssertContains("GenerateLookupKeys: 'Pokemon Red and Blue' contains 'pokemon red'",
            keys, "pokemon red");
        AssertContains("GenerateLookupKeys: 'Pokemon Red and Blue' contains 'pokemon blue'",
            keys, "pokemon blue");
        AssertContains("GenerateLookupKeys: 'Pokemon Red and Blue' contains canonical",
            keys, "pokemon red and blue");

        var keys2 = NameMatcher.GenerateLookupKeys("Pokemon FireRed and LeafGreen");
        AssertContains("GenerateLookupKeys: 'Pokemon FireRed and LeafGreen' contains 'pokemon firered'",
            keys2, "pokemon firered");
        AssertContains("GenerateLookupKeys: 'Pokemon FireRed and LeafGreen' contains 'pokemon leafgreen'",
            keys2, "pokemon leafgreen");
    }

    private static void TestGenerateVariants()
    {
        var variants = NameMatcher.GenerateVariants("Pokemon Blue");
        AssertContains("GenerateVariants: 'Pokemon Blue' contains itself",
            variants, "pokemon blue");
    }

    private static void TestKnownAliases()
    {
        var canonical = KnownAliases.TryGetCanonical("pokemon blue");
        Assert("KnownAliases: 'pokemon blue' -> 'Pokemon Red and Blue'",
            canonical, "Pokemon Red and Blue");

        Assert("KnownAliases: 'pokemon firered' -> 'Pokemon FireRed and LeafGreen'",
            KnownAliases.TryGetCanonical("pokemon firered"), "Pokemon FireRed and LeafGreen");

        Assert("KnownAliases: 'ff7' -> 'Final Fantasy VII'",
            KnownAliases.TryGetCanonical("ff7"), "Final Fantasy VII");

        Assert("KnownAliases: unknown returns null",
            KnownAliases.TryGetCanonical("totally fake game"), null);
    }

    private static void TestDatasetLookups(string dir)
    {
        Console.WriteLine($"Loading dataset from {dir}...");
        var sw = Stopwatch.StartNew();
        var ds = new HltbDataSet(dir);
        ds.Load();
        sw.Stop();
        Console.WriteLine($"Loaded {ds.EntryCount} entries in {sw.ElapsedMilliseconds}ms");

        // Direct hit - exact title should beat suffix variants like "Remake"
        AssertDatasetMatch(ds, "Final Fantasy VII", null, "Final Fantasy VII");

        // Suffix variant should match its own entry, not the original
        AssertDatasetMatch(ds, "Final Fantasy VII Remake", null, "Final Fantasy VII Remake");

        // Diacritic test
        AssertDatasetMatch(ds, "Pokémon Crystal", null, "Pokemon Crystal");

        // Pokemon dual-release via known alias
        AssertDatasetMatchContains(ds, "Pokemon Blue", null, "Pokemon Red and Blue");
        AssertDatasetMatchContains(ds, "Pokemon Red", null, "Pokemon Red and Blue");

        // FireRed
        AssertDatasetMatchContains(ds, "Pokemon FireRed", null, "Pokemon FireRed");
        AssertDatasetMatchContains(ds, "Pokemon LeafGreen", "Game Boy Advance", "FireRed");

        // Pokemon Yellow stays alone (not dual)
        AssertDatasetMatchContains(ds, "Pokemon Yellow", null, "Pokemon Yellow");

        // Gold/Silver
        AssertDatasetMatchContains(ds, "Pokemon Gold", null, "Pokemon Gold and Silver");
        AssertDatasetMatchContains(ds, "Pokemon Silver", null, "Pokemon Gold and Silver");

        // Diamond/Pearl
        AssertDatasetMatchContains(ds, "Pokemon Diamond", null, "Pokemon Diamond and Pearl");

        // Variant suffix
        AssertDatasetMatchContains(ds, "Pokemon Emerald Version", null, "Emerald");

        // Common titles
        AssertDatasetMatchContains(ds, "Skyrim", null, "Skyrim");
        AssertDatasetMatchContains(ds, "The Witcher 3: Wild Hunt", null, "Witcher 3");
        AssertDatasetMatchContains(ds, "The Legend of Zelda: Ocarina of Time", null, "Ocarina of Time");

        // Abbreviation alias
        AssertDatasetMatchContains(ds, "FF7", null, "Final Fantasy VII");
        AssertDatasetMatchContains(ds, "BotW", null, "Breath of the Wild");

        // ROM-style names with edition suffix
        AssertDatasetMatchContains(ds, "Super Mario World (USA)", null, "Super Mario World");

        // Subtitle prefix like "007: Quantum of Solace" should match the subtitle "Quantum of Solace"
        AssertDatasetMatchContains(ds, "007: Quantum of Solace", "Sony Playstation 2", "Quantum of Solace");

        // Platform name case insensitivity: "Sony Playstation 2" (LB) should map to "PlayStation 2" (HLTB)
        Assert("PlatformMappings: case-insensitive (Sony Playstation 2)",
            PlatformMappings.Map("Sony Playstation 2"), "PlayStation 2");
        Assert("PlatformMappings: case-insensitive (sony playstation 2)",
            PlatformMappings.Map("sony playstation 2"), "PlayStation 2");
    }

    // ===== Test helpers =====

    private static void Assert<T>(string name, T actual, T expected)
    {
        var ok = Equals(actual, expected);
        if (ok) { _pass++; Console.WriteLine($"  PASS: {name}"); }
        else { _fail++; Console.WriteLine($"  FAIL: {name}\n        expected: '{expected}'\n        actual:   '{actual}'"); }
    }

    private static void AssertList(string name, System.Collections.Generic.List<string> actual, string[] expected)
    {
        var actualArr = actual.ToArray();
        var ok = actualArr.Length == expected.Length;
        if (ok)
        {
            for (int i = 0; i < expected.Length; i++)
            {
                if (!string.Equals(actualArr[i], expected[i], StringComparison.OrdinalIgnoreCase))
                {
                    ok = false; break;
                }
            }
        }
        if (ok) { _pass++; Console.WriteLine($"  PASS: {name}"); }
        else { _fail++; Console.WriteLine($"  FAIL: {name}\n        expected: [{string.Join(", ", expected)}]\n        actual:   [{string.Join(", ", actualArr)}]"); }
    }

    private static void AssertContains(string name, System.Collections.Generic.List<string> list, string expected)
    {
        if (list.Contains(expected, StringComparer.OrdinalIgnoreCase))
        {
            _pass++; Console.WriteLine($"  PASS: {name}");
        }
        else
        {
            _fail++; Console.WriteLine($"  FAIL: {name}\n        wanted: '{expected}'\n        in:     [{string.Join(", ", list)}]");
        }
    }

    private static void AssertDatasetMatch(HltbDataSet ds, string query, string? platform, string expectedExact)
    {
        var entry = ds.Lookup(query, platform);
        var actualNormalized = entry != null ? NameMatcher.StripDiacritics(entry.Name) : "";
        var ok = entry != null && string.Equals(actualNormalized, expectedExact, StringComparison.OrdinalIgnoreCase);
        if (ok) { _pass++; Console.WriteLine($"  PASS: Lookup '{query}' [{platform}] -> '{entry!.Name}'"); }
        else { _fail++; Console.WriteLine($"  FAIL: Lookup '{query}' [{platform}]\n        expected: '{expectedExact}'\n        actual:   '{(entry?.Name ?? "null")}'"); }
    }

    private static void AssertDatasetMatchContains(HltbDataSet ds, string query, string? platform, string expectedSubstring)
    {
        var entry = ds.Lookup(query, platform);
        var actualNormalized = entry != null ? NameMatcher.StripDiacritics(entry.Name) : "";
        var ok = entry != null && actualNormalized.Contains(expectedSubstring, StringComparison.OrdinalIgnoreCase);
        if (ok) { _pass++; Console.WriteLine($"  PASS: Lookup '{query}' [{platform}] -> '{entry!.Name}' (matched '{expectedSubstring}')"); }
        else { _fail++; Console.WriteLine($"  FAIL: Lookup '{query}' [{platform}]\n        wanted substring: '{expectedSubstring}'\n        actual:           '{(entry?.Name ?? "null")}'"); }
    }
}
