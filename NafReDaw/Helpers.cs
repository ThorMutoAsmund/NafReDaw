namespace NafReDaw;

using NafMidi;

public static class Helpers
{
    public static string[] SplitCommandLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return [];

        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var c in line.Trim())
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result.ToArray();
    }

    public static bool TryParseNote(string text, out byte note)
    {
        note = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var commaIndex = text.IndexOf(',');
        if (commaIndex >= 0)
        {
            var rowText = text[..commaIndex].Trim();
            var columnText = text[(commaIndex + 1)..].Trim();

            if (!int.TryParse(columnText, out var column) || !int.TryParse(rowText, out var row))
            {
                return false;
            }

            if (row < 0 || row >= LaunchpadLayout.GridRows || column < 0 || column >= LaunchpadLayout.GridColumns)
            {
                return false;
            }

            note = (byte)LaunchpadLayout.NoteFromGrid(row, column);
            return true;
        }

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return byte.TryParse(text.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out note);
        }

        return byte.TryParse(text, out note);
    }
}
