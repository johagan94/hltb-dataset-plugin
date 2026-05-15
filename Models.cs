using System.Collections.Generic;

namespace HltbDatasetPlugin;

public class HltbEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Platform { get; set; } = "";
    public string Genres { get; set; } = "";
    public string Developer { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string ReleaseDate { get; set; } = "";
    public int ReleaseYear { get; set; }
    public int ReleaseMonth { get; set; }
    public int ReleaseDay { get; set; }

    public int MainStoryPolled { get; set; }
    public double MainStory { get; set; }
    public int MainPlusSidesPolled { get; set; }
    public double MainPlusSides { get; set; }
    public int CompletionistPolled { get; set; }
    public double Completionist { get; set; }
    public int AllStylesPolled { get; set; }
    public double AllStyles { get; set; }
    public int SinglePlayerPolled { get; set; }
    public double SinglePlayer { get; set; }
    public int CoOpPolled { get; set; }
    public double CoOp { get; set; }
    public int VersusPolled { get; set; }
    public double Versus { get; set; }

    public string SourceUrl { get; set; } = "";

    /// <summary>Normalized name for matching (lowercase, stripped punctuation, trimmed).</summary>
    public string NormalizedName { get; set; } = "";

    /// <summary>Platform list as separate entries for matching.</summary>
    public string[] PlatformList { get; set; } = System.Array.Empty<string>();

    public bool HasMainStory => MainStoryPolled > 0 && MainStory > 0;
    public bool HasMainPlusSides => MainPlusSidesPolled > 0 && MainPlusSides > 0;
    public bool HasCompletionist => CompletionistPolled > 0 && Completionist > 0;
    public bool HasAnyData => HasMainStory || HasMainPlusSides || HasCompletionist;
}

/// <summary>Platform name mappings from LaunchBox conventions to HLTB CSV conventions.</summary>
public static class PlatformMappings
{
    public static readonly Dictionary<string, string> LaunchBoxToHltb = new(System.StringComparer.OrdinalIgnoreCase)
    {
        // Nintendo
        { "Nintendo Entertainment System", "NES" },
        { "Super Nintendo Entertainment System", "Super Nintendo" },
        { "Nintendo 64", "Nintendo 64" },
        { "Nintendo GameCube", "GameCube" },
        { "Nintendo Wii", "Wii" },
        { "Nintendo Wii U", "Wii U" },
        { "Nintendo Switch", "Nintendo Switch" },
        { "Nintendo Game Boy", "Game Boy" },
        { "Nintendo Game Boy Color", "Game Boy Color" },
        { "Nintendo Game Boy Advance", "Game Boy Advance" },
        { "Nintendo DS", "Nintendo DS" },
        { "Nintendo 3DS", "Nintendo 3DS" },

        // Sony (dict is case-insensitive so one entry covers all capitalizations)
        { "Sony PlayStation", "PlayStation" },
        { "PlayStation", "PlayStation" },
        { "Sony PlayStation 2", "PlayStation 2" },
        { "PlayStation 2", "PlayStation 2" },
        { "Sony PlayStation 3", "PlayStation 3" },
        { "PlayStation 3", "PlayStation 3" },
        { "Sony PlayStation 4", "PlayStation 4" },
        { "PlayStation 4", "PlayStation 4" },
        { "Sony PlayStation 5", "PlayStation 5" },
        { "PlayStation 5", "PlayStation 5" },
        { "Sony PSP", "PlayStation Portable" },
        { "Sony PlayStation Portable", "PlayStation Portable" },
        { "Sony PlayStation Vita", "PlayStation Vita" },

        // Microsoft
        { "Microsoft Xbox", "Xbox" },
        { "Microsoft Xbox 360", "Xbox 360" },
        { "Microsoft Xbox One", "Xbox One" },
        { "Microsoft Xbox Series X/S", "Xbox Series X/S" },

        // Sega
        { "Sega Genesis", "Genesis" },
        { "Sega Master System", "Sega Master System" },
        { "Sega Game Gear", "Game Gear" },
        { "Sega Dreamcast", "Dreamcast" },
        { "Sega Saturn", "Saturn" },

        // Other
        { "Windows", "PC" },
        { "PC", "PC" },
        { "MS-DOS", "PC" },
        { "Linux", "PC" },
        { "Macintosh", "PC" },
        { "Mac OS", "PC" },
        { "Arcade", "Arcade" },
        { "Atari 2600", "Atari 2600" },
        { "Atari 7800", "Atari 7800" },
        { "Commodore 64", "Commodore 64" },
        { "NEC TurboGrafx-16", "TurboGrafx-16" },
        { "NEC TurboGrafx-CD", "TurboGrafx-CD" },
        { "SNK Neo Geo", "Neo Geo" },
        { "SNK Neo Geo Pocket", "Neo Geo Pocket" },
        { "SNK Neo Geo Pocket Color", "Neo Geo Pocket Color" },
        { "3DO Interactive Multiplayer", "3DO" },
        { "Atari Jaguar", "Jaguar" },
        { "Philips CD-i", "Philips CD-i" },
        { "Mobile", "Mobile" },
        { "Android", "Mobile" },
        { "iOS", "Mobile" },
        { "Browser", "Browser" },
    };

    /// <summary>Try map a LaunchBox platform name to its HLTB equivalent. Returns the original if no mapping found.</summary>
    public static string Map(string launchboxPlatform)
    {
        if (string.IsNullOrEmpty(launchboxPlatform)) return "";
        return LaunchBoxToHltb.TryGetValue(launchboxPlatform, out var mapped) ? mapped : launchboxPlatform;
    }
}
