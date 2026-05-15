using System.Collections.Generic;

namespace HltbDatasetPlugin;

/// <summary>
/// Curated dictionary mapping common LaunchBox/ROM names to their HLTB canonical names.
/// These are cases that the automatic dual-split logic can't handle reliably.
/// Keys should be normalized (lowercase, no diacritics, no punctuation).
/// </summary>
public static class KnownAliases
{
    public static readonly Dictionary<string, string> Map = new()
    {
        // Pokemon dual-releases
        { "pokemon red", "Pokemon Red and Blue" },
        { "pokemon blue", "Pokemon Red and Blue" },
        { "pokemon red version", "Pokemon Red and Blue" },
        { "pokemon blue version", "Pokemon Red and Blue" },
        { "pokemon yellow", "Pokemon Yellow" },
        { "pokemon yellow version", "Pokemon Yellow" },
        { "pokemon gold", "Pokemon Gold and Silver" },
        { "pokemon silver", "Pokemon Gold and Silver" },
        { "pokemon gold version", "Pokemon Gold and Silver" },
        { "pokemon silver version", "Pokemon Gold and Silver" },
        { "pokemon crystal", "Pokemon Crystal" },
        { "pokemon crystal version", "Pokemon Crystal" },
        { "pokemon ruby", "Pokemon Ruby and Sapphire" },
        { "pokemon sapphire", "Pokemon Ruby and Sapphire" },
        { "pokemon emerald", "Pokemon Emerald Version" },
        { "pokemon firered", "Pokemon FireRed and LeafGreen" },
        { "pokemon leafgreen", "Pokemon FireRed and LeafGreen" },
        { "pokemon fire red", "Pokemon FireRed and LeafGreen" },
        { "pokemon leaf green", "Pokemon FireRed and LeafGreen" },
        { "pokemon diamond", "Pokemon Diamond and Pearl" },
        { "pokemon pearl", "Pokemon Diamond and Pearl" },
        { "pokemon platinum", "Pokemon Platinum" },
        { "pokemon heartgold", "Pokemon HeartGold and SoulSilver" },
        { "pokemon soulsilver", "Pokemon HeartGold and SoulSilver" },
        { "pokemon heart gold", "Pokemon HeartGold and SoulSilver" },
        { "pokemon soul silver", "Pokemon HeartGold and SoulSilver" },
        { "pokemon black", "Pokemon Black and White" },
        { "pokemon white", "Pokemon Black and White" },
        { "pokemon black 2", "Pokemon Black and White 2" },
        { "pokemon white 2", "Pokemon Black and White 2" },
        { "pokemon x", "Pokemon X and Y" },
        { "pokemon y", "Pokemon X and Y" },
        { "pokemon omega ruby", "Pokemon Omega Ruby and Alpha Sapphire" },
        { "pokemon alpha sapphire", "Pokemon Omega Ruby and Alpha Sapphire" },
        { "pokemon sun", "Pokemon Sun and Moon" },
        { "pokemon moon", "Pokemon Sun and Moon" },
        { "pokemon ultra sun", "Pokemon Ultra Sun and Ultra Moon" },
        { "pokemon ultra moon", "Pokemon Ultra Sun and Ultra Moon" },
        { "pokemon sword", "Pokemon Sword and Shield" },
        { "pokemon shield", "Pokemon Sword and Shield" },
        { "pokemon scarlet", "Pokemon Scarlet and Violet" },
        { "pokemon violet", "Pokemon Scarlet and Violet" },
        { "pokemon lets go pikachu", "Pokemon Let's Go, Pikachu! and Let's Go, Eevee!" },
        { "pokemon lets go eevee", "Pokemon Let's Go, Pikachu! and Let's Go, Eevee!" },
        { "pokemon brilliant diamond", "Pokemon Brilliant Diamond and Shining Pearl" },
        { "pokemon shining pearl", "Pokemon Brilliant Diamond and Shining Pearl" },

        // Mystery Dungeon
        { "pokemon mystery dungeon blue rescue team", "Pokemon Mystery Dungeon: Blue/Red Rescue Team" },
        { "pokemon mystery dungeon red rescue team", "Pokemon Mystery Dungeon: Blue/Red Rescue Team" },
        { "pokemon mystery dungeon explorers of time", "Pokemon Mystery Dungeon: Explorers of Time and Explorers of Darkness" },
        { "pokemon mystery dungeon explorers of darkness", "Pokemon Mystery Dungeon: Explorers of Time and Explorers of Darkness" },

        // Common abbreviations
        { "ff7", "Final Fantasy VII" },
        { "ffvii", "Final Fantasy VII" },
        { "ff8", "Final Fantasy VIII" },
        { "ffviii", "Final Fantasy VIII" },
        { "ff9", "Final Fantasy IX" },
        { "ffix", "Final Fantasy IX" },
        { "ff10", "Final Fantasy X" },
        { "ffx", "Final Fantasy X" },
        { "mgs", "Metal Gear Solid" },
        { "mgs2", "Metal Gear Solid 2: Sons of Liberty" },
        { "mgs3", "Metal Gear Solid 3: Snake Eater" },
        { "mgs4", "Metal Gear Solid 4: Guns of the Patriots" },
        { "mgs5", "Metal Gear Solid V: The Phantom Pain" },
        { "gta3", "Grand Theft Auto III" },
        { "gta iii", "Grand Theft Auto III" },
        { "gta vc", "Grand Theft Auto: Vice City" },
        { "gta sa", "Grand Theft Auto: San Andreas" },
        { "gta4", "Grand Theft Auto IV" },
        { "gta iv", "Grand Theft Auto IV" },
        { "gta5", "Grand Theft Auto V" },
        { "gta v", "Grand Theft Auto V" },
        { "ssb", "Super Smash Bros." },
        { "ssbm", "Super Smash Bros. Melee" },
        { "ssbb", "Super Smash Bros. Brawl" },
        { "smb", "Super Mario Bros." },
        { "smb2", "Super Mario Bros. 2" },
        { "smb3", "Super Mario Bros. 3" },
        { "smw", "Super Mario World" },
        { "loz", "The Legend of Zelda" },
        { "oot", "The Legend of Zelda: Ocarina of Time" },
        { "mm", "The Legend of Zelda: Majora's Mask" },
        { "ww", "The Legend of Zelda: The Wind Waker" },
        { "tp", "The Legend of Zelda: Twilight Princess" },
        { "ss", "The Legend of Zelda: Skyward Sword" },
        { "botw", "The Legend of Zelda: Breath of the Wild" },
        { "totk", "The Legend of Zelda: Tears of the Kingdom" },
    };

    /// <summary>Get the canonical HLTB name for a normalized lookup name, or null.</summary>
    public static string? TryGetCanonical(string normalizedName)
    {
        return Map.TryGetValue(normalizedName, out var canonical) ? canonical : null;
    }
}
