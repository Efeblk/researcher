using System.Globalization;
using System.Net;
using System.Text;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;

internal static class AcademicWorkTitleNormalizer
{
    private static readonly HashSet<string> PresentationalTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "b", "big", "br", "em", "i", "mark", "s", "small", "span", "strike", "strong",
        "sub", "sup", "u"
    };

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string decoded = WebUtility.HtmlDecode(value);
        string withoutMarkup = RemovePresentationalTags(decoded);
        StringBuilder normalized = new();
        bool pendingSpace = false;
        foreach (Rune rune in withoutMarkup.Normalize(NormalizationForm.FormKD).EnumerateRunes())
        {
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (category == UnicodeCategory.NonSpacingMark || IsIgnorableFormat(rune))
                continue;
            if (rune.Value == 0x200B)
            {
                pendingSpace = true;
                continue;
            }
            if (Rune.IsLetterOrDigit(rune))
            {
                AppendPendingSpace(normalized, ref pendingSpace);
                Rune lower = Rune.ToLowerInvariant(rune);
                normalized.Append(lower.Value == 0x131 ? 'i' : lower.ToString());
            }
            else if (category is UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol or
                UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol)
            {
                AppendPendingSpace(normalized, ref pendingSpace);
                normalized.Append(rune.ToString());
            }
            else
                pendingSpace = true;
        }
        return normalized.Length == 0 ? null : normalized.ToString();
    }

    private static bool IsIgnorableFormat(Rune rune) =>
        Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format && rune.Value != 0x200B;

    private static void AppendPendingSpace(StringBuilder normalized, ref bool pendingSpace)
    {
        if (pendingSpace && normalized.Length != 0)
            normalized.Append(' ');
        pendingSpace = false;
    }

    private static string RemovePresentationalTags(string value)
    {
        List<(int Start, int End, bool LineBreak)> removals = [];
        Dictionary<string, Stack<(int Start, int End)>> openTags =
            new(StringComparer.OrdinalIgnoreCase);
        for (int scanPosition = 0; scanPosition < value.Length; scanPosition++)
        {
            if (value[scanPosition] != '<' ||
                !TryReadPresentationalTag(value, scanPosition, out ParsedTag tag))
                continue;
            scanPosition = tag.End;
            if (tag.Name.Equals("br", StringComparison.OrdinalIgnoreCase))
            {
                removals.Add((tag.Start, tag.End, true));
                continue;
            }
            if (!tag.IsClosing)
            {
                if (!tag.IsSelfClosing)
                {
                    if (!openTags.TryGetValue(tag.Name, out Stack<(int Start, int End)>? starts))
                        openTags[tag.Name] = starts = new();
                    starts.Push((tag.Start, tag.End));
                }
                continue;
            }
            if (!openTags.TryGetValue(tag.Name, out Stack<(int Start, int End)>? matching) ||
                !matching.TryPop(out (int Start, int End) opening))
                continue;
            removals.Add((opening.Start, opening.End, false));
            removals.Add((tag.Start, tag.End, false));
        }

        if (removals.Count == 0)
            return value;
        removals.Sort((left, right) => left.Start.CompareTo(right.Start));
        StringBuilder result = new(value.Length);
        int position = 0;
        foreach ((int start, int end, bool lineBreak) in removals)
        {
            result.Append(value, position, start - position);
            if (lineBreak)
                result.Append(' ');
            position = end + 1;
        }
        result.Append(value, position, value.Length - position);
        return result.ToString();
    }

    private static bool TryReadPresentationalTag(string value, int start, out ParsedTag tag)
    {
        tag = default;
        int end = FindTagEnd(value, start + 1);
        if (end < 0)
            return false;

        ReadOnlySpan<char> content = value.AsSpan(start + 1, end - start - 1);
        if (content.IsEmpty || char.IsWhiteSpace(content[0]))
            return false;
        bool isClosing = content[0] == '/';
        if (isClosing)
        {
            content = content[1..];
            if (content.IsEmpty || char.IsWhiteSpace(content[0]))
                return false;
        }
        int nameLength = 0;
        while (nameLength < content.Length && char.IsAsciiLetter(content[nameLength]))
            nameLength++;
        if (nameLength == 0 || nameLength < content.Length &&
            !char.IsWhiteSpace(content[nameLength]) && content[nameLength] != '/')
            return false;

        string name = content[..nameLength].ToString();
        if (!PresentationalTags.Contains(name))
            return false;
        ReadOnlySpan<char> remainder = content[nameLength..].Trim();
        bool isSelfClosing = !remainder.IsEmpty && remainder[^1] == '/';
        if (isClosing && !remainder.IsEmpty)
            return false;
        if (!isClosing && !IsValidAttributeList(remainder))
            return false;
        tag = new(start, end, name, isClosing, isSelfClosing);
        return true;
    }

    private static bool IsValidAttributeList(ReadOnlySpan<char> value)
    {
        int position = 0;
        while (position < value.Length)
        {
            while (position < value.Length && char.IsWhiteSpace(value[position]))
                position++;
            if (position == value.Length || position == value.Length - 1 && value[position] == '/')
                return true;
            int nameStart = position;
            while (position < value.Length && (char.IsAsciiLetterOrDigit(value[position]) ||
                value[position] is '-' or '_' or ':' or '.'))
                position++;
            if (position == nameStart)
                return false;
            while (position < value.Length && char.IsWhiteSpace(value[position]))
                position++;
            if (position == value.Length || value[position] != '=')
                continue;
            position++;
            while (position < value.Length && char.IsWhiteSpace(value[position]))
                position++;
            if (position == value.Length)
                return false;
            if (value[position] is '\'' or '"')
            {
                char quote = value[position++];
                while (position < value.Length && value[position] != quote)
                    position++;
                if (position == value.Length)
                    return false;
                position++;
            }
            else
            {
                int valueStart = position;
                while (position < value.Length && !char.IsWhiteSpace(value[position]) &&
                    value[position] != '/')
                    position++;
                if (position == valueStart)
                    return false;
            }
        }
        return true;
    }

    private static int FindTagEnd(string value, int start)
    {
        char quote = '\0';
        for (int index = start; index < value.Length; index++)
        {
            char character = value[index];
            if (quote != '\0')
            {
                if (character == quote)
                    quote = '\0';
                continue;
            }
            if (character is '\'' or '"')
                quote = character;
            else if (character == '>')
                return index;
        }
        return -1;
    }

    private readonly record struct ParsedTag(
        int Start,
        int End,
        string Name,
        bool IsClosing,
        bool IsSelfClosing);
}
