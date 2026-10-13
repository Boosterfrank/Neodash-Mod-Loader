namespace Neodash_Mod_Loader;

public static class ModVersion
{
    public static int? Compare(string? left, string? right)
    {
        if (!TryParse(left, out var a, out var aPre) || !TryParse(right, out var b, out var bPre)) return null;
        int result = CompareNumbers(a, b);
        return result != 0 ? result : ComparePrerelease(aPre, bPre);
    }

    private static int CompareNumbers(ulong[] left, ulong[] right)
    {
        for (int i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            int result = (i < left.Length ? left[i] : 0).CompareTo(i < right.Length ? right[i] : 0);
            if (result != 0) return result;
        }
        return 0;
    }

    private static int ComparePrerelease(string[] left, string[] right)
    {
        if (left.Length == 0) return right.Length == 0 ? 0 : 1;
        if (right.Length == 0) return -1;
        for (int i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            int result = ComparePrereleaseIdentifier(left[i], right[i]);
            if (result != 0) return result;
        }
        return left.Length.CompareTo(right.Length);
    }

    private static int ComparePrereleaseIdentifier(string left, string right)
    {
        bool leftNumeric = ulong.TryParse(left, out ulong leftNumber);
        bool rightNumeric = ulong.TryParse(right, out ulong rightNumber);
        if (leftNumeric && rightNumeric) return leftNumber.CompareTo(rightNumber);
        if (leftNumeric != rightNumeric) return leftNumeric ? -1 : 1;
        return string.CompareOrdinal(left, right);
    }

    private static bool TryParse(string? value, out ulong[] numbers, out string[] prerelease)
    {
        numbers = [];
        prerelease = [];
        if (string.IsNullOrWhiteSpace(value)) return false;
        string text = value.Trim().TrimStart('v', 'V').Split('+')[0];
        var parts = text.Split('-', 2);
        var digits = parts[0].Split('.');
        numbers = new ulong[digits.Length];
        for (int i = 0; i < digits.Length; i++) if (!ulong.TryParse(digits[i], out numbers[i])) return false;
        if (parts.Length == 2)
        {
            prerelease = parts[1].Split('.');
            foreach (string part in prerelease)
                if (part.Length == 0 || part.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) return false;
        }
        return true;
    }
}
