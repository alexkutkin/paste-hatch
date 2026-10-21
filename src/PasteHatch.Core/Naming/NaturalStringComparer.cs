namespace PasteHatch.Core;

/// <summary>
/// Explorer-like ordering for names: case-insensitive, with digit runs compared by value so
/// "file2" comes before "file10". A plain ordinal sort would put "file10" first.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var left = 0;
        var right = 0;

        while (left < x.Length && right < y.Length)
        {
            if (char.IsAsciiDigit(x[left]) && char.IsAsciiDigit(y[right]))
            {
                var leftStart = left;
                var rightStart = right;

                while (left < x.Length && char.IsAsciiDigit(x[left]))
                {
                    left++;
                }

                while (right < y.Length && char.IsAsciiDigit(y[right]))
                {
                    right++;
                }

                var numeric = CompareNumericRuns(
                    x.AsSpan(leftStart, left - leftStart),
                    y.AsSpan(rightStart, right - rightStart));
                if (numeric != 0)
                {
                    return numeric;
                }

                continue;
            }

            var leftChar = char.ToUpperInvariant(x[left]);
            var rightChar = char.ToUpperInvariant(y[right]);
            if (leftChar != rightChar)
            {
                return leftChar.CompareTo(rightChar);
            }

            left++;
            right++;
        }

        var length = (x.Length - left).CompareTo(y.Length - right);
        return length != 0 ? length : string.CompareOrdinal(x, y);
    }

    private static int CompareNumericRuns(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        var leftSignificant = left.TrimStart('0');
        var rightSignificant = right.TrimStart('0');

        if (leftSignificant.Length != rightSignificant.Length)
        {
            return leftSignificant.Length.CompareTo(rightSignificant.Length);
        }

        var value = leftSignificant.SequenceCompareTo(rightSignificant);
        if (value != 0)
        {
            return value;
        }

        // Same numeric value but different zero padding ("02" vs "2"): keep a stable order.
        return left.Length.CompareTo(right.Length);
    }
}
