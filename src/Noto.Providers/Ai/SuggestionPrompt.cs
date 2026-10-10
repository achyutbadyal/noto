using Noto.Core.Ai;
using Noto.Core.Links;

namespace Noto.Providers.Ai;

// The one place the model's instructions live. Kept in code (not a file) so a new field is a single
// edit, and the parser is written against the same key names.
public static class SuggestionPrompt
{
    public const string System = """
        You turn a rough task title into structured fields for a to-do app. You are given the user's raw
        text and today's date. Reply with a single JSON object and nothing else.

        Keys (omit any you cannot infer — never guess a value):
          "title"            string   a short imperative rewrite; keep the user's own words where clear
          "estimate_minutes" integer  a realistic focused-work estimate
          "priority"         integer  0 none, 1 highest … 4 lowest
          "planned_for"      string   "YYYY-MM-DD" the day the user intends to do it
          "due_date"         string   "YYYY-MM-DD" an external deadline, only if the text states one
          "time_of_day"      string   one of morning, midday, afternoon, evening
          "waiting_on"       string   a person or team the task is blocked on, only if stated
          "notes"            string   extra detail worth keeping, only if the text contains some
          "tags"             array    lowercase single words
          "confidence"       number   0..1, how sure you are overall
          "rationale"        string   one short sentence explaining the fields you filled

        Rules:
        - Resolve relative dates ("tomorrow", "friday", "next week") against today's date.
        - The title must not repeat a date, time or duration you put in another key. "Call the dentist
          tomorrow morning" becomes "Call the dentist" with planned_for and time_of_day set.
        - If a linked page is given, treat it as fact and prefer it over your own guess about what the URL
          means. Write the title from what the page actually is. If it says the link could not be read,
          do not invent its contents.
        - Never invent a deadline, a person or a tag that the text does not imply.
        - Omit a key rather than guessing.
        - Output JSON only, with no surrounding text and no code fences.
        """;

    public static AiPrompt For(SuggestionRequest request) =>
        new(
            System,
            $"Today is {request.Today:yyyy-MM-dd}.\n"
                + (request.Link is { } link ? "\n" + LinkBlock(link) : "")
                + $"\nRaw text:\n{request.Text}"
        );

    // The resolved page gets its own block so the model treats it as fact rather than inferring meaning
    // from a bare URL. A link that could not be read says so, so the model doesn't fill the gap itself.
    static string LinkBlock(LinkContext link)
    {
        var lines = new List<string> { $"The task links to {link.Url}" };
        if (link.Unreadable is { } why)
        {
            lines.Add($"That page could not be read ({why}), so do not guess what it contains.");
            return string.Join('\n', lines) + "\n";
        }

        if (link.Provider is { } provider)
            lines.Add($"  Source: {provider}");
        if (link.Title is { } title)
            lines.Add($"  Title: {title}");
        if (link.State is { } state)
            lines.Add($"  State: {state}");
        if (link.Facts.Count > 0)
            lines.Add($"  Facts: {string.Join(" · ", link.Facts)}");
        if (link.Summary is { } summary)
            lines.Add($"  Summary: {summary}");
        lines.Add("This is the most reliable signal about what the task is.");
        return string.Join('\n', lines) + "\n";
    }
}
