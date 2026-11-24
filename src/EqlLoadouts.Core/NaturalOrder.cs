namespace EqlLoadouts.Core;

/// <summary>
/// Orders text alphabetically and numbers by value, so Page2 comes before Page10.
/// </summary>
public static class NaturalOrder
{
    public static int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        var i = 0;
        var j = 0;
        while (i < left.Length && j < right.Length)
        {
            if (char.IsDigit(left[i]) && char.IsDigit(right[j]))
            {
                var compared = CompareNumber(left, ref i, right, ref j);
                if (compared != 0)
                {
                    return compared;
                }

                continue;
            }

            var leftStart = i;
            var rightStart = j;
            while (i < left.Length && !char.IsDigit(left[i]))
            {
                i++;
            }

            while (j < right.Length && !char.IsDigit(right[j]))
            {
                j++;
            }

            var comparedText = CompareText(left, leftStart, i - leftStart, right, rightStart, j - rightStart);
            if (comparedText != 0)
            {
                return comparedText;
            }
        }

        if (i == left.Length && j == right.Length)
        {
            return string.CompareOrdinal(left, right);
        }

        return i == left.Length ? -1 : 1;
    }

    private static int CompareNumber(string left, ref int i, string right, ref int j)
    {
        var leftRaw = i;
        var rightRaw = j;
        while (i < left.Length && left[i] == '0')
        {
            i++;
        }

        while (j < right.Length && right[j] == '0')
        {
            j++;
        }

        var leftDigits = i;
        var rightDigits = j;
        while (i < left.Length && char.IsDigit(left[i]))
        {
            i++;
        }

        while (j < right.Length && char.IsDigit(right[j]))
        {
            j++;
        }

        var leftLength = i - leftDigits;
        var rightLength = j - rightDigits;
        if (leftLength != rightLength)
        {
            return leftLength.CompareTo(rightLength);
        }

        if (leftLength > 0)
        {
            var compared = string.CompareOrdinal(left, leftDigits, right, rightDigits, leftLength);
            if (compared != 0)
            {
                return compared;
            }
        }

        return (i - leftRaw).CompareTo(j - rightRaw);
    }

    private static int CompareText(string left, int leftStart, int leftLength, string right, int rightStart, int rightLength)
    {
        var shared = Math.Min(leftLength, rightLength);
        var compared = string.Compare(left, leftStart, right, rightStart, shared, StringComparison.OrdinalIgnoreCase);
        if (compared != 0)
        {
            return compared;
        }

        return leftLength.CompareTo(rightLength);
    }
}
