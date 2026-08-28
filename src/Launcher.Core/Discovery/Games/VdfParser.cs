using System.Text;

namespace Launcher.Core.Discovery.Games;

/// <summary>
/// A node in a Valve KeyValues document: either a leaf with a string value, or a set of
/// child nodes.
/// </summary>
public sealed class VdfNode
{
    private readonly Dictionary<string, VdfNode> _children =
        new(StringComparer.OrdinalIgnoreCase);

    internal VdfNode()
    {
    }

    internal VdfNode(string value) => Value = value;

    /// <summary>The leaf value, or null for a node with children.</summary>
    public string? Value { get; }

    public IReadOnlyDictionary<string, VdfNode> Children => _children;

    /// <summary>Child by key, or null. Keys are matched case-insensitively, as Valve does.</summary>
    public VdfNode? this[string key] =>
        _children.TryGetValue(key, out VdfNode? child) ? child : null;

    /// <summary>Walks a path of keys and returns the leaf value, or null if any step is missing.</summary>
    public string? GetString(params string[] path)
    {
        VdfNode? node = this;

        foreach (string key in path)
        {
            node = node?[key];
        }

        return node?.Value;
    }

    internal void Set(string key, VdfNode node) => _children[key] = node;
}

/// <summary>
/// Reads Valve's KeyValues text format, which Steam uses for <c>libraryfolders.vdf</c> and
/// every <c>appmanifest_*.acf</c>.
/// <para>
/// Deliberately lenient. These files are written by Steam and read by us; a format we do
/// not fully understand should cost one game, not the whole scan, so anything unparseable
/// is skipped rather than thrown on.
/// </para>
/// </summary>
public static class VdfParser
{
    /// <summary>Guards against a pathological or hostile file recursing without end.</summary>
    private const int MaxDepth = 32;

    /// <summary>Parses a document. Returns null when there is nothing usable in it.</summary>
    public static VdfNode? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        int index = 0;
        var root = new VdfNode();

        try
        {
            ReadInto(root, text, ref index, depth: 0, expectClosingBrace: false);
        }
        catch (Exception)
        {
            // Return whatever was read before the malformed part.
        }

        return root.Children.Count > 0 ? root : null;
    }

    private static void ReadInto(VdfNode parent, string text, ref int index, int depth, bool expectClosingBrace)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        while (true)
        {
            SkipTrivia(text, ref index);

            if (index >= text.Length)
            {
                return;
            }

            if (text[index] == '}')
            {
                index++;

                if (expectClosingBrace)
                {
                    return;
                }

                // A stray closing brace: ignore it and keep going.
                continue;
            }

            string? key = ReadToken(text, ref index);
            if (key is null)
            {
                return;
            }

            SkipTrivia(text, ref index);

            if (index < text.Length && text[index] == '{')
            {
                index++;
                var child = new VdfNode();
                ReadInto(child, text, ref index, depth + 1, expectClosingBrace: true);
                parent.Set(key, child);
                continue;
            }

            string? value = ReadToken(text, ref index);
            if (value is null)
            {
                return;
            }

            parent.Set(key, new VdfNode(value));
        }
    }

    /// <summary>Skips whitespace and both comment styles Valve files use.</summary>
    private static void SkipTrivia(string text, ref int index)
    {
        while (index < text.Length)
        {
            char c = text[index];

            if (char.IsWhiteSpace(c))
            {
                index++;
                continue;
            }

            if (c == '/' && index + 1 < text.Length && text[index + 1] == '/')
            {
                while (index < text.Length && text[index] is not ('\n' or '\r'))
                {
                    index++;
                }

                continue;
            }

            return;
        }
    }

    private static string? ReadToken(string text, ref int index)
    {
        SkipTrivia(text, ref index);

        if (index >= text.Length)
        {
            return null;
        }

        return text[index] == '"'
            ? ReadQuoted(text, ref index)
            : ReadBare(text, ref index);
    }

    private static string ReadQuoted(string text, ref int index)
    {
        index++; // opening quote
        var builder = new StringBuilder();

        while (index < text.Length)
        {
            char c = text[index];

            if (c == '\\' && index + 1 < text.Length)
            {
                // Paths in these files are written with doubled backslashes.
                char next = text[index + 1];
                builder.Append(next switch
                {
                    'n' => '\n',
                    't' => '\t',
                    _ => next,
                });

                index += 2;
                continue;
            }

            if (c == '"')
            {
                index++;
                return builder.ToString();
            }

            builder.Append(c);
            index++;
        }

        return builder.ToString();
    }

    private static string ReadBare(string text, ref int index)
    {
        int start = index;

        while (index < text.Length && !char.IsWhiteSpace(text[index]) && text[index] is not ('{' or '}'))
        {
            index++;
        }

        return text[start..index];
    }
}
