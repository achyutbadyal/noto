namespace Noto.App.Logic;

public static class FuzzyMatch
{
    // 0 = no match. Higher is better: prefix and word-start hits beat scattered subsequence hits.
    public static int Score(string query, string candidate)
    {
        if (query.Length == 0)
            return 1;
        var q = query.ToLowerInvariant();
        var c = candidate.ToLowerInvariant();

        if (c == q)
            return 1000;
        if (c.StartsWith(q))
            return 800 - c.Length;
        if (c.Contains(' ' + q))
            return 600 - c.Length;
        if (c.Contains(q))
            return 400 - c.Length;

        // Subsequence: every query char in order; consecutive runs score higher.
        var score = 0;
        var pos = 0;
        var run = 0;
        foreach (var ch in q)
        {
            var found = c.IndexOf(ch, pos);
            if (found < 0)
                return 0;
            run = found == pos ? run + 1 : 1;
            score += 10 * run;
            pos = found + 1;
        }
        return Math.Max(1, score - c.Length);
    }
}
