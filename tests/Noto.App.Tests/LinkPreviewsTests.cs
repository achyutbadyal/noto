using Noto.App.Services;
using Noto.Core.Links;

namespace Noto.App.Tests;

// What a linked URL shows on a row and in the inspector, and when its preview is asked for again.
public sealed class LinkPreviewsTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-07T10:00:00Z");
    const string Ticket = "https://acme.atlassian.net/browse/PROJ-88";

    static LinkPreview Jira(PreviewStatus status = PreviewStatus.Loaded) =>
        new()
        {
            Url = Ticket,
            ProviderId = "jira",
            Title = "Login fails on Safari",
            Subtitle = "PROJ-88",
            ChipFacts = [new("In Review"), new("@sam"), new("Priority High")],
            AuthorName = "sam",
            Snippet = "Happens only on iOS 19.",
            Status = status,
        };

    [Fact]
    public void A_loaded_jira_preview_carries_its_key_status_and_assignee()
    {
        var line = LinkLine.From(Ticket, Jira(), "Jira");

        line.Title.ShouldBe("Login fails on Safari");
        line.Provider.ShouldBe("Jira");
        line.Subtitle.ShouldBe("PROJ-88");
        line.Facts.ShouldBe(["In Review", "@sam", "Priority High"]);
        line.Author.ShouldBe("sam");
        line.Snippet.ShouldBe("Happens only on iOS 19.");
        line.HasStatus.ShouldBeFalse();
    }

    [Fact]
    public void The_row_reads_key_title_and_first_status_in_one_line()
    {
        LinkLine
            .From(Ticket, Jira(), "Jira")
            .RowText.ShouldBe("PROJ-88 · Login fails on Safari · In Review");
    }

    [Fact]
    public void A_link_without_a_preview_shows_its_host_and_says_it_is_not_fetched()
    {
        var line = LinkLine.From(Ticket, null, "Web link");

        line.Title.ShouldBe("acme.atlassian.net");
        line.Facts.ShouldBeEmpty();
        line.Status.ShouldBe("Not fetched yet");
        line.RowText.ShouldBe("acme.atlassian.net");
    }

    [Fact]
    public void A_preview_that_needs_a_connection_names_the_provider()
    {
        var line = LinkLine.From(Ticket, Jira(PreviewStatus.AuthRequired), "Jira");

        line.Status.ShouldBe("Connect Jira in Settings to see this");
    }

    [Fact]
    public void A_missing_object_keeps_the_reason_the_provider_gave()
    {
        var p = Jira(PreviewStatus.Unavailable);
        p.ErrorMessage = "No longer accessible";

        LinkLine.From(Ticket, p, "Jira").Status.ShouldBe("No longer accessible");
    }

    [Fact]
    public void Long_snippets_are_cut_to_a_short_excerpt()
    {
        var p = Jira();
        p.Snippet = new string('a', 500);

        var snippet = LinkLine.From(Ticket, p, "Jira").Snippet!;

        snippet.Length.ShouldBe(240);
        snippet.EndsWith('…').ShouldBeTrue();
    }

    [Fact]
    public void A_loaded_preview_is_due_once_it_expires()
    {
        var p = Jira();
        p.ExpiresAt = Now.AddMinutes(10);

        LinkPreviews.IsDue(p, Now).ShouldBeFalse();
        LinkPreviews.IsDue(p, Now.AddMinutes(11)).ShouldBeTrue();
    }

    [Fact]
    public void A_failed_preview_waits_before_it_is_asked_again()
    {
        var p = Jira(PreviewStatus.Error);
        p.FetchedAt = Now;

        LinkPreviews.IsDue(p, Now.AddMinutes(1)).ShouldBeFalse();
        LinkPreviews.IsDue(p, Now.AddMinutes(3)).ShouldBeTrue();
    }

    [Fact]
    public void A_preview_that_needs_a_connection_is_asked_again_after_the_retry_window()
    {
        var p = Jira(PreviewStatus.AuthRequired);
        p.FetchedAt = Now;

        LinkPreviews.IsDue(p, Now.AddMinutes(3)).ShouldBeTrue();
    }

    static LinkLine Line(string url, string stateHash, string viewedHash) =>
        LinkLine.From(
            url,
            new LinkPreview
            {
                Url = url,
                ProviderId = "jira",
                Title = "Login fails on Safari",
                Status = PreviewStatus.Loaded,
                StateHash = stateHash,
                ViewedStateHash = viewedHash,
            },
            "Jira"
        );

    [Fact]
    public void A_link_whose_state_moved_since_the_user_looked_is_flagged_as_changed()
    {
        Line(Ticket, "In Review", "Open").HasChange.ShouldBeTrue();
    }

    [Fact]
    public void A_link_still_at_the_state_the_user_looked_at_is_not_flagged()
    {
        Line(Ticket, "In Review", "In Review").HasChange.ShouldBeFalse();
    }

    [Fact]
    public void A_link_with_no_preview_has_no_change_to_show()
    {
        LinkLine.From(Ticket, null, "Web link").HasChange.ShouldBeFalse();
    }

    // The dot is invisible to a screen reader, so the chip says it instead.
    [Fact]
    public void A_changed_link_says_so_in_its_accessible_name()
    {
        Line(Ticket, "In Review", "Open")
            .AccessibleName.ShouldBe("Login fails on Safari (changed)");
        Line(Ticket, "In Review", "In Review").AccessibleName.ShouldBe("Login fails on Safari");
    }

    [Fact]
    public void Only_the_links_that_moved_are_marked_seen()
    {
        var moved = Line(Ticket, "In Review", "Open");
        var still = Line("https://github.com/noto/noto/pull/1", "open", "open");

        LinkPreviews.Moved([moved, still]).ShouldBe([Ticket]);
    }

    // Re-stamping every link on every navigation would erase the dot for links nobody looked at.
    [Fact]
    public void Nothing_is_marked_seen_when_no_link_moved()
    {
        LinkPreviews.Moved([Line(Ticket, "In Review", "In Review")]).ShouldBeEmpty();
        LinkPreviews.Moved([]).ShouldBeEmpty();
    }
}
