namespace DitaStudio.Core.Diff;

public enum DiffKind { Equal, Removed, Added }

public sealed record DiffLine(DiffKind Kind, string? Left, string? Right);

/// <summary>Построчное сравнение текста двух файлов (LCS) — как обычный текстовый diff,
/// без разбора XML в DOM: различия форматирования тоже показываются, как в git diff.</summary>
public static class XmlDiff
{
    // ponytail: классический DP LCS, O(n*m) времени и памяти. Для карт/топиков разумного
    // размера хватает; на файлах в тысячи строк потребуется алгоритм Майерса (O(ND)).
    public static IReadOnlyList<DiffLine> Compare(string leftText, string rightText)
    {
        var left = Normalize(leftText);
        var right = Normalize(rightText);
        var n = left.Length;
        var m = right.Length;

        var dp = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                dp[i, j] = left[i] == right[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        var result = new List<DiffLine>();
        var a = 0;
        var b = 0;
        while (a < n && b < m)
        {
            if (left[a] == right[b])
            {
                result.Add(new DiffLine(DiffKind.Equal, left[a], right[b]));
                a++;
                b++;
            }
            else if (dp[a + 1, b] >= dp[a, b + 1])
            {
                result.Add(new DiffLine(DiffKind.Removed, left[a], null));
                a++;
            }
            else
            {
                result.Add(new DiffLine(DiffKind.Added, null, right[b]));
                b++;
            }
        }

        while (a < n)
        {
            result.Add(new DiffLine(DiffKind.Removed, left[a], null));
            a++;
        }

        while (b < m)
        {
            result.Add(new DiffLine(DiffKind.Added, null, right[b]));
            b++;
        }

        return result;
    }

    public static string[] Normalize(string text) => text.Replace("\r\n", "\n").Split('\n');
}
