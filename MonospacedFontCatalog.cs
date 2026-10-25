using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SourceCodeEditor;

/// <summary>
/// Enumerates the actual font families installed on this Windows machine.
/// Only regular-style families with approximately equal Latin glyph advances
/// appear in the monospaced menus. No hard-coded or imaginary font entries.
/// </summary>
internal static class MonospacedFontCatalog
{
    private static readonly TextFormatFlags MeasurementFlags =
        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

    private static readonly Lazy<IReadOnlyList<string>> InstalledFonts = new(EnumerateInstalled);

    public static IReadOnlyList<string> ListInstalled() => InstalledFonts.Value;

    private static IReadOnlyList<string> EnumerateInstalled()
    {
        var names = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        using var installed = new InstalledFontCollection();
        foreach (FontFamily family in installed.Families)
        {
            try
            {
                if (!family.IsStyleAvailable(FontStyle.Regular)) continue;
                using var sample = new Font(family, 12f, FontStyle.Regular, GraphicsUnit.Point);
                if (IsMonospaced(sample)) names.Add(family.Name);
            }
            catch (ArgumentException) { /* Damaged/unusable installed font. */ }
            catch (ExternalException) { /* Unreadable Windows font resource. */ }
        }
        return names.ToArray();
    }

    public static bool IsMonospaced(Font font)
    {
        // Measuring long runs minimizes one-pixel GDI hinting differences.
        // Letters, digits and punctuation are compared so proportional fonts
        // cannot be mistaken for fixed-width fonts by checking only M and W.
        const int count = 12;
        char[] glyphs = { 'i', 'W', 'M', '0', '.', 'l', 'm' };
        int minimumWidth = int.MaxValue;
        int maximumWidth = 0;
        foreach (char glyph in glyphs)
        {
            int width = TextRenderer.MeasureText(new string(glyph, count), font,
                new Size(10000, 200), MeasurementFlags).Width;
            minimumWidth = Math.Min(minimumWidth, width);
            maximumWidth = Math.Max(maximumWidth, width);
        }
        return minimumWidth > 0 && maximumWidth - minimumWidth <= 2;
    }

    public static string InstalledOrDefault(string? familyName, string fallback)
    {
        if (string.IsNullOrWhiteSpace(familyName)) return fallback;
        using var installed = new InstalledFontCollection();
        foreach (var installedFamily in installed.Families)
        {
            try
            {
                if (installedFamily.IsStyleAvailable(FontStyle.Regular) &&
                    string.Equals(installedFamily.Name, familyName, StringComparison.OrdinalIgnoreCase))
                    return installedFamily.Name;
            }
            catch (ArgumentException) { }
            catch (ExternalException) { }
        }
        return fallback;
    }
}
