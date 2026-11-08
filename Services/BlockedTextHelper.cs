using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LanguageData.Models;

namespace LanguageData.Services;

public static class BlockedTextHelper
{
    private static readonly Regex MultipleSpacesRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex CleanQuotesRegex = new(@"^[\s""'„“”‚‘’`´]+|[\s""'„“”‚‘’`´]+$", RegexOptions.Compiled);
    private static readonly Regex ProtocolAndWwwRegex = new(@"^(?:https?://)?(?:www\d*\.)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Normalisiert Eingaben, entfernt unsichtbare Zero-Width-Zeichen (Umgehungsschutz),
    /// vereinheitlicht Apostrophe/Bindestriche und decodiert URL-Prozent-Codierungen (%20 etc.).
    /// </summary>
    public static string NormalizeEntry(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string text = DecodeUrlSafely(value);

        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            // Unsichtbare Zero-Width-Zeichen (Umgehungsschutz) restlos entfernen
            if (c is '\u200B' or '\u200C' or '\u200D' or '\uFEFF' or '\u00AD')
                continue;

            sb.Append(c);
        }
        text = sb.ToString();

        // 1. Apostroph- und Akzent-Varianten vereinheitlichen -> ' (U+0027)
        text = text
            .Replace('\u2019', '\'')
            .Replace('\u2018', '\'')
            .Replace('\u00B4', '\'')
            .Replace('`', '\'')
            .Replace('\u02BC', '\'');

        // 2. Gedankenstriche vereinheitlichen -> -
        text = text
            .Replace('\u2013', '-')
            .Replace('\u2014', '-')
            .Replace('\u2212', '-');

        // 3. Geschützte Leerzeichen und mehrfache Leerzeichen vereinheitlichen
        text = text.Replace('\u00A0', ' ');
        text = MultipleSpacesRegex.Replace(text.Trim(), " ");

        // 4. Äußere Anführungszeichen entfernen
        text = CleanQuotesRegex.Replace(text, "");

        return text.Trim();
    }

    /// <summary>
    /// Decodiert URL-codierte Strings (z.B. %20, %C3%BC) sicher in Klartext.
    /// </summary>
    public static string DecodeUrlSafely(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        string current = input;
        try
        {
            if (current.Contains('%'))
            {
                current = Uri.UnescapeDataString(current);
            }
        }
        catch
        {
            // Falls ungültige %-Sequenzen enthalten sind, Originaltext behalten
        }

        return current;
    }

    /// <summary>
    /// Entfernt https://, http://, www. und abschließende Slashes.
    /// </summary>
    public static string StripUrlProtocolAndWww(string value)
    {
        string s = ProtocolAndWwwRegex.Replace(value.Trim(), "");
        return s.Trim('/');
    }

    /// <summary>
    /// Parst eine Batch-Eingabe (mehrere Zeilen, Semikolon- oder Komma-getrennt).
    /// Erkennt [AGG], [AGGRESSIV], *, [NORM], [EXAKT] sowie Aufzählungszeichen (- oder •).
    /// 100% kompatibel mit Windows- und Notizen-Exporten.
    /// </summary>
    public static List<BlockedKeyword> ParseBatchInput(string? input, bool defaultAggressive = false)
    {
        var result = new Dictionary<string, BlockedKeyword>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(input))
            return new List<BlockedKeyword>();

        // Trennen nach Newlines und Semikolons
        var rawLines = input
            .Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        // Falls die Eingabe nur 1 Zeile ist, aber Kommas enthält (z.B. "wort1, wort2, wort3"):
        if (rawLines.Count == 1 && rawLines[0].Contains(',') && !rawLines[0].Contains("://"))
        {
            var commaTokens = rawLines[0].Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s));
            rawLines = commaTokens.ToList();
        }

        foreach (var rawLine in rawLines)
        {
            string line = rawLine;

            // Aufzählungszeichen am Zeilenanfang entfernen (- , • )
            if (line.StartsWith("- ") || line.StartsWith("• "))
                line = line[2..].Trim();

            bool isAggressive = defaultAggressive;

            if (line.StartsWith("[AGGRESSIV]", StringComparison.OrdinalIgnoreCase))
            {
                isAggressive = true;
                line = line[11..].Trim();
            }
            else if (line.StartsWith("[AGG]", StringComparison.OrdinalIgnoreCase))
            {
                isAggressive = true;
                line = line[5..].Trim();
            }
            else if (line.StartsWith("[EXAKT]", StringComparison.OrdinalIgnoreCase))
            {
                isAggressive = false;
                line = line[7..].Trim();
            }
            else if (line.StartsWith("[NORM]", StringComparison.OrdinalIgnoreCase))
            {
                isAggressive = false;
                line = line[6..].Trim();
            }
            else if (line.StartsWith("*") && line.Length > 1)
            {
                isAggressive = true;
                line = line[1..].Trim();
            }

            string cleanValue = NormalizeEntry(line);
            if (string.IsNullOrWhiteSpace(cleanValue))
                continue;

            string stripped = StripUrlProtocolAndWww(cleanValue);
            if (!string.IsNullOrWhiteSpace(stripped))
                cleanValue = stripped;

            if (result.TryGetValue(cleanValue, out var existing))
            {
                if (isAggressive)
                    existing.IsAggressive = true;
            }
            else
            {
                result[cleanValue] = new BlockedKeyword
                {
                    Value = cleanValue,
                    IsAggressive = isAggressive
                };
            }
        }

        return result.Values.ToList();
    }

    /// <summary>
    /// Exportiert alle gesperrten Blockwörter in das standardisierte Clipboard-Backup-Format
    /// (1 Zeile = 1 Eintrag, [AGG] für aggressive Begriffe).
    /// </summary>
    public static string FormatForBackupClipboard(IEnumerable<BlockedKeyword>? items)
    {
        if (items == null) return string.Empty;
        var sb = new StringBuilder();
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Value)) continue;
            string clean = NormalizeEntry(item.Value);
            if (string.IsNullOrWhiteSpace(clean)) continue;

            if (item.IsAggressive)
                sb.AppendLine($"[AGG] {clean}");
            else
                sb.AppendLine(clean);
        }
        return sb.ToString().TrimEnd();
    }
}
