using System.Globalization;
using System.Text;

namespace PortSyncs.Api.Domain;

/// <summary>
/// Truck plates are stored as (country, normalized). Display format is a country setting (T64).
/// Normalization keeps letters and digits of any script (e.g. Arabic plate letters), upper-cased,
/// with spaces, dashes and separators removed, and Arabic-Indic digits mapped to 0-9.
/// </summary>
public static class Plate
{
    public static string Normalize(string? plate)
    {
        if (string.IsNullOrWhiteSpace(plate)) return string.Empty;

        var sb = new StringBuilder(plate.Length);
        foreach (var c in plate.Normalize(NormalizationForm.FormKC))
        {
            if (!char.IsLetterOrDigit(c)) continue;
            var digit = CharUnicodeInfo.GetDecimalDigitValue(c);
            sb.Append(digit >= 0 ? (char)('0' + digit) : char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }
}
