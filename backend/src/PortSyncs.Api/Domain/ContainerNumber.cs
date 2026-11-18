namespace PortSyncs.Api.Domain;

/// <summary>
/// ISO 6346 container number: 3-letter owner code, category letter (U, J or Z),
/// 6-digit serial and a check digit. Example: MSKU0496560.
/// </summary>
public readonly record struct ContainerNumber(string Value)
{
    // Letter values skip multiples of 11 (11, 22, 33).
    private static readonly int[] LetterValues =
    [
        10, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 23, 24,
        25, 26, 27, 28, 29, 30, 31, 32, 34, 35, 36, 37, 38,
    ];

    public static bool TryParse(string? input, out ContainerNumber result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var s = new string(input.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        if (s.Length != 11) return false;

        for (var i = 0; i < 4; i++)
            if (s[i] < 'A' || s[i] > 'Z') return false;
        if (s[3] is not ('U' or 'J' or 'Z')) return false;
        for (var i = 4; i < 11; i++)
            if (!char.IsAsciiDigit(s[i])) return false;

        if (CheckDigit(s) != s[10] - '0') return false;

        result = new ContainerNumber(s);
        return true;
    }

    public static int CheckDigit(string number)
    {
        var sum = 0;
        for (var i = 0; i < 10; i++)
        {
            var c = number[i];
            var value = char.IsAsciiDigit(c) ? c - '0' : LetterValues[c - 'A'];
            sum += value << i;
        }
        return sum % 11 % 10;
    }

    public override string ToString() => Value;
}
