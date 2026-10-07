using Noto.Core.Commands;

namespace Noto.App.Logic;

public enum TokenKind
{
    Date,
    Size,
    Priority,
    Tag,
    Waiting,
    Workspace,
}

public sealed record CaptureToken(TokenKind Kind, string Text, int Start);

public sealed record ParsedCapture(
    string Title,
    DateOnly? PlannedFor,
    int? EstimateMinutes,
    int Priority,
    IReadOnlyList<string> Tags,
    string? WaitingOn,
    string? WorkspaceName,
    IReadOnlyList<CaptureToken> Tokens
);

// Recognizes the inline capture tokens from docs/07 §7.1:  tomorrow ~30m !2 #backend @waiting:priya /work
public static class TokenParser
{
    public static readonly IReadOnlyDictionary<string, int> DefaultSizes = new Dictionary<
        string,
        int
    >
    {
        ["s"] = 15,
        ["m"] = 60,
        ["l"] = 180,
    };

    public static ParsedCapture Parse(
        string input,
        DateOnly today,
        IReadOnlyDictionary<string, int>? sizes = null,
        bool recognizeTags = true
    )
    {
        sizes ??= DefaultSizes;
        var words = Split(input);
        var tokens = new List<CaptureToken>();
        var title = new List<string>();
        DateOnly? date = null;
        int? estimate = null;
        var priority = 0;
        var tags = new List<string>();
        string? waiting = null,
            workspace = null;

        for (var i = 0; i < words.Count; i++)
        {
            var (text, start) = words[i];

            if (TrySize(text, sizes) is { } minutes)
            {
                estimate = minutes;
                tokens.Add(new(TokenKind.Size, text, start));
                continue;
            }
            if (text.Length == 2 && text[0] == '!' && text[1] is >= '1' and <= '4')
            {
                priority = text[1] - '0';
                tokens.Add(new(TokenKind.Priority, text, start));
                continue;
            }
            // Tags start with a letter, so references like #482 stay in the title. Capture passes
            // recognizeTags: false because nothing stores a tag yet — better to keep "#backend" visible
            // in the title than to strip it and lose it.
            if (
                recognizeTags
                && text.Length > 1
                && text[0] == '#'
                && char.IsLetter(text[1])
            )
            {
                tags.Add(text[1..]);
                tokens.Add(new(TokenKind.Tag, text, start));
                continue;
            }
            if (text.StartsWith("@waiting:", StringComparison.OrdinalIgnoreCase) && text.Length > 9)
            {
                waiting = text[9..];
                tokens.Add(new(TokenKind.Waiting, text, start));
                continue;
            }
            if (
                text.Length > 1
                && text[0] == '/'
                && text.Skip(1).All(c => char.IsLetterOrDigit(c) || c is '-' or '_')
            )
            {
                workspace = text[1..];
                tokens.Add(new(TokenKind.Workspace, text, start));
                continue;
            }

            // "next week" / "next fri" span two words, so try the pair first.
            if (
                i + 1 < words.Count
                && NaturalDate.TryParse($"{text} {words[i + 1].Text}", today, out var pair)
            )
            {
                date = pair;
                tokens.Add(new(TokenKind.Date, $"{text} {words[i + 1].Text}", start));
                i++;
                continue;
            }
            if (NaturalDate.TryParse(text, today, out var single))
            {
                date = single;
                tokens.Add(new(TokenKind.Date, text, start));
                continue;
            }

            title.Add(text);
        }

        return new ParsedCapture(
            string.Join(' ', title),
            date,
            estimate,
            priority,
            tags,
            waiting,
            workspace,
            tokens
        );
    }

    // `~15m`, `~2h`, `~1h30m`, `~s|m|l`
    static int? TrySize(string text, IReadOnlyDictionary<string, int> sizes)
    {
        if (text.Length < 2 || text[0] != '~')
            return null;
        var body = text[1..].ToLowerInvariant();
        if (sizes.TryGetValue(body, out var preset))
            return preset;

        var total = 0;
        var number = 0;
        var sawDigit = false;
        foreach (var c in body)
        {
            if (char.IsDigit(c))
            {
                number = number * 10 + (c - '0');
                sawDigit = true;
                continue;
            }
            if (!sawDigit)
                return null;
            total += c switch
            {
                'h' => number * 60,
                'm' => number,
                _ => -1,
            };
            if (c is not ('h' or 'm'))
                return null;
            (number, sawDigit) = (0, false);
        }
        if (sawDigit)
            return null; // trailing digits without a unit
        return total > 0 ? total : null;
    }

    static List<(string Text, int Start)> Split(string input)
    {
        var words = new List<(string, int)>();
        for (var i = 0; i < input.Length; )
        {
            while (i < input.Length && char.IsWhiteSpace(input[i]))
                i++;
            var start = i;
            while (i < input.Length && !char.IsWhiteSpace(input[i]))
                i++;
            if (i > start)
                words.Add((input[start..i], start));
        }
        return words;
    }

    // Backspace removes a whole recognized token at the end of the input.
    public static string RemoveTrailingToken(string input, DateOnly today, bool recognizeTags = true)
    {
        var trimmed = input.TrimEnd();
        var last = Parse(trimmed, today, recognizeTags: recognizeTags).Tokens.LastOrDefault();
        if (last is null || last.Start + last.Text.Length != trimmed.Length)
            return input.Length > 0 ? input[..^1] : input;
        return trimmed[..last.Start];
    }
}
