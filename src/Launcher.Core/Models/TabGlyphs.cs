namespace Launcher.Core.Models;

/// <summary>One pickable tab icon.</summary>
/// <param name="Glyph">The character to render in Segoe Fluent Icons.</param>
/// <param name="Name">Label for the picker and for screen readers.</param>
public sealed record TabGlyph(string Glyph, string Name);

/// <summary>
/// The tab icon set: monochrome Segoe Fluent Icons glyphs, not emoji.
/// <para>
/// Emoji were the wrong choice. They drag a second font and a colour palette into what is
/// otherwise a monochrome Fluent interface, and they look nothing like the rest of the
/// chrome. Extracted <em>app</em> icons stay in full colour - those are the apps' own
/// branding - but the launcher's own iconography is monochrome throughout.
/// </para>
/// <para>
/// Glyphs are built from their code points rather than written as literals so the source
/// stays readable and nothing depends on the file's encoding. Every code point here was
/// checked against the shipped font.
/// </para>
/// </summary>
public static class TabGlyphs
{
    /// <summary>Start of the Unicode private use area, where all Fluent glyphs live.</summary>
    private const int PrivateUseStart = 0xE000;

    private const int PrivateUseEnd = 0xF8FF;

    private static readonly (int CodePoint, string Name)[] Definitions =
    [
        (0xE80F, "Home"),
        (0xE7FC, "Games"),
        (0xE7B8, "Apps"),
        (0xE8B7, "Folder"),
        (0xE82D, "Library"),
        (0xE943, "Code"),
        (0xE9D9, "Developer"),
        (0xE90F, "Tools"),
        (0xE713, "Settings"),
        (0xE774, "Web"),
        (0xE715, "Mail"),
        (0xE8BD, "Chat"),
        (0xE8D6, "Music"),
        (0xE7EE, "Movies"),
        (0xE714, "Video"),
        (0xE722, "Camera"),
        (0xE8EF, "Calculator"),
        (0xE7C3, "Documents"),
        (0xE8F1, "Lists"),
        (0xE753, "Cloud"),
        (0xE83D, "Security"),
        (0xE734, "Favourites"),
        (0xEB51, "Personal"),
        (0xE77B, "People"),
        (0xE718, "Pinned"),
        (0xEC92, "Recent"),
        (0xE945, "Utilities"),
        (0xE930, "Automation"),
        (0xE896, "Downloads"),
        (0xE7C1, "Flagged"),
        (0xE81E, "Maps"),
        (0xECAA, "Other"),
    ];

    /// <summary>Every glyph the picker offers, in display order.</summary>
    public static IReadOnlyList<TabGlyph> All { get; } =
        [.. Definitions.Select(d => new TabGlyph(char.ConvertFromUtf32(d.CodePoint), d.Name))];

    /// <summary>The Home tab's icon.</summary>
    public static string Home { get; } = char.ConvertFromUtf32(0xE80F);

    /// <summary>
    /// True when the value is a Fluent icon glyph. Anything else - most obviously an emoji
    /// left over from an older version - is replaced on load.
    /// </summary>
    public static bool IsFluentGlyph(string? glyph)
    {
        if (string.IsNullOrEmpty(glyph))
        {
            return false;
        }

        // A Fluent glyph is exactly one BMP character in the private use area. Emoji are
        // either outside that range or a surrogate pair, so both are rejected.
        return glyph.Length == 1
            && glyph[0] >= PrivateUseStart
            && glyph[0] <= PrivateUseEnd;
    }
}
