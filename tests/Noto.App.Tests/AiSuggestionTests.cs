using System.ComponentModel;
using Noto.App.Logic;
using Noto.App.Services;
using Noto.App.ViewModels;
using Noto.Core.Ai;
using Noto.Core.Links;
using Noto.Core.Models;
using Noto.Platform.Abstractions;
using Noto.Providers.Ai;

namespace Noto.App.Tests;

// The fill writes the create form, never a task. These pin that contract: what gets written, what is
// left alone, that a link is resolved before the model sees it, that the fill can be undone, and that
// switching AI off removes every affordance.
public sealed class AiSuggestionTests : IDisposable
{
    readonly AppFixture _app = new();

    public void Dispose() => _app.Dispose();

    AddItemViewModel Form(ISuggestionService? suggestions, ILinkResolver? links = null) =>
        new(
            _app.Services.Runner,
            _app.Workspace.Id,
            plannedForToday: true,
            null,
            () => AppFixture.Today,
            suggestions,
            links
        );

    [Fact]
    public void The_sparkle_is_hidden_when_ai_is_off()
    {
        Form(null).HasSuggestions.ShouldBeFalse();
        Form(NullSuggestionService.Instance).HasSuggestions.ShouldBeFalse();
    }

    [Fact]
    public void The_sparkle_appears_when_a_provider_is_enabled()
    {
        Form(new FakeSuggestionService { Enabled = true }).HasSuggestions.ShouldBeTrue();
    }

    [Fact]
    public void Toggling_ai_off_updates_an_already_open_form()
    {
        var service = new FakeSuggestionService { Enabled = true };
        var form = Form(service);

        form.HasSuggestions.ShouldBeTrue();
        service.SetEnabled(false);
        form.HasSuggestions.ShouldBeFalse();
    }

    [Fact]
    public async Task Suggest_fills_the_form_and_never_creates_a_task()
    {
        var service = new FakeSuggestionService
        {
            Enabled = true,
            Result = new FieldSuggestion
            {
                Title = "Call the dentist",
                EstimateMinutes = 30,
                Priority = 2,
                PlannedFor = AppFixture.Today.AddDays(1),
                DueDate = AppFixture.Today.AddDays(3),
                TimeOfDay = TimeOfDay.Morning,
                WaitingOn = "Priya",
                Notes = "Ask about the crown",
                Confidence = 0.7,
                Rationale = "The text names a day and a person.",
            },
        };
        var form = Form(service);
        form.DetailTitle = "call dentist tomorrow ~30m, waiting on Priya";

        await form.SuggestCommand.ExecuteAsync(null);

        form.DetailTitle.ShouldBe("Call the dentist");
        form.DetailEstimate.ShouldBe(FieldOptions.DurationFor(30));
        form.DetailPriority.ShouldBe(FieldOptions.PriorityFor(2));
        form.DetailWhen.ShouldBe(FieldOptions.Whens[1]); // Tomorrow
        form.DetailDue.ShouldBe(FieldOptions.DueFor(AppFixture.Today.AddDays(3), AppFixture.Today));
        form.DetailTime.ShouldBe(FieldOptions.TimeOfDayFor(TimeOfDay.Morning));
        form.DetailWaitingOn.ShouldBe("Priya");
        form.DetailNotes.ShouldBe("Ask about the crown");
        form.SuggestionNote!.ShouldContain("names a day");

        // Nothing was saved: the fill is a draft for the user to review.
        form.LastCreatedId.ShouldBeNull();
        (await _app.Services.Reader.LoadAsync(_app.Workspace.Id)).Items.ShouldNotContain(i =>
            i.Title == "Call the dentist"
        );
    }

    [Fact]
    public async Task Suggest_only_overwrites_the_fields_the_model_filled()
    {
        var service = new FakeSuggestionService
        {
            Enabled = true,
            Result = new FieldSuggestion { Title = "Renamed by AI" }, // nothing else
        };
        var form = Form(service);
        form.DetailTitle = "rough text";
        form.DetailPriority = FieldOptions.Priorities[3]; // the user's own choice
        form.DetailNotes = "my note";

        await form.SuggestCommand.ExecuteAsync(null);

        form.DetailTitle.ShouldBe("Renamed by AI");
        form.DetailPriority.ShouldBe(FieldOptions.Priorities[3]);
        form.DetailNotes.ShouldBe("my note");
    }

    [Fact]
    public async Task A_failed_fill_is_reported_and_leaves_the_form_alone()
    {
        var service = new FakeSuggestionService
        {
            Enabled = true,
            Throw = new AiSuggestionException("The API key was rejected."),
        };
        var form = Form(service);
        form.DetailTitle = "call the dentist";

        await form.SuggestCommand.ExecuteAsync(null);

        form.SuggestionError.ShouldBe("The API key was rejected.");
        form.DetailTitle.ShouldBe("call the dentist");
        form.IsSuggesting.ShouldBeFalse();
    }

    [Fact]
    public async Task An_empty_title_is_reported_without_calling_the_model()
    {
        var service = new FakeSuggestionService { Enabled = true };
        var form = Form(service);
        form.DetailTitle = "   ";

        await form.SuggestCommand.ExecuteAsync(null);

        form.SuggestionError!.ShouldContain("title");
        service.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_reply_with_nothing_usable_is_reported_not_applied()
    {
        var service = new FakeSuggestionService
        {
            Enabled = true,
            Result = new FieldSuggestion(), // everything null
        };
        var form = Form(service);
        form.DetailTitle = "hmm";

        await form.SuggestCommand.ExecuteAsync(null);

        form.SuggestionError.ShouldNotBeNullOrWhiteSpace();
        form.DetailTitle.ShouldBe("hmm");
    }

    [Fact]
    public async Task Editing_the_title_clears_the_previous_fill_note()
    {
        var service = new FakeSuggestionService
        {
            Enabled = true,
            Result = new FieldSuggestion { Title = "A", Rationale = "because" },
        };
        var form = Form(service);
        form.DetailTitle = "a";
        await form.SuggestCommand.ExecuteAsync(null);
        form.SuggestionNote.ShouldNotBeNullOrWhiteSpace();

        form.DetailTitle = "a different title";

        form.SuggestionNote.ShouldBeNull();
    }

    [Fact]
    public async Task A_URL_in_the_title_is_resolved_before_the_model_sees_it()
    {
        var resolver = new FakeLinkResolver
        {
            Result = new LinkContext(
                "https://github.com/acme/api/pull/482",
                "Fix login redirect",
                "GitHub",
                "Closes #471",
                "Merged",
                ["#482", "merged 2h ago"],
                null
            ),
        };
        var service = new FakeSuggestionService
        {
            Enabled = true,
            Result = new FieldSuggestion { Title = "Review the login fix" },
        };
        var form = Form(service, resolver);
        form.DetailTitle = "review https://github.com/acme/api/pull/482";

        await form.SuggestCommand.ExecuteAsync(null);

        resolver.Asked.ShouldContain("https://github.com/acme/api/pull/482");
        service.Last.ShouldNotBeNull();
        service.Last!.Link.ShouldNotBeNull();
        service.Last.Link!.Title.ShouldBe("Fix login redirect");
        form.DetailTitle.ShouldBe("Review the login fix");
    }

    [Fact]
    public async Task A_resolver_failure_is_fail_open_the_model_still_gets_the_raw_text()
    {
        var resolver = new FakeLinkResolver { Throw = new Exception("network") };
        var service = new FakeSuggestionService
        {
            Enabled = true,
            Result = new FieldSuggestion { Title = "Review link" },
        };
        var form = Form(service, resolver);
        form.DetailTitle = "review https://example.com/page";

        await form.SuggestCommand.ExecuteAsync(null);

        service.Last.ShouldNotBeNull();
        service.Last!.Text.ShouldBe("review https://example.com/page");
        service.Last.Link.ShouldNotBeNull();
        service.Last.Link!.Url.ShouldBe("https://example.com/page");
        service.Last.Link.Unreadable.ShouldNotBeNull();
        form.DetailTitle.ShouldBe("Review link");
    }

    [Fact]
    public async Task No_URL_means_no_resolver_call()
    {
        var resolver = new FakeLinkResolver();
        var service = new FakeSuggestionService
        {
            Enabled = true,
            Result = new FieldSuggestion { Title = "Call dentist" },
        };
        var form = Form(service, resolver);
        form.DetailTitle = "call the dentist";

        await form.SuggestCommand.ExecuteAsync(null);

        resolver.Asked.ShouldBeEmpty();
        service.Last!.Link.ShouldBeNull();
    }
}

// Device-level AI configuration: the mode/key rules and the persistence round-trip.
public sealed class AiOptionsTests
{
    [Fact]
    public void A_fresh_install_has_ai_off()
    {
        var ai = new AiOptions(new InMemoryUiState(), new InMemoryKeyring(), new HttpClient());

        ai.Mode.ShouldBe(AiMode.Off);
        ai.IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Local_needs_no_key_but_cloud_does()
    {
        var ai = new AiOptions(new InMemoryUiState(), new InMemoryKeyring(), new HttpClient());

        await ai.SaveAsync(AiMode.Local, "http://localhost:11434/v1", "llama3", null);
        ai.IsEnabled.ShouldBeTrue();

        await ai.SaveAsync(AiMode.Cloud, "https://api.openai.com/v1", "gpt-4o-mini", null);
        ai.IsEnabled.ShouldBeFalse("cloud is useless without a key");

        await ai.SaveAsync(AiMode.Cloud, "https://api.openai.com/v1", "gpt-4o-mini", "sk-test");
        ai.IsEnabled.ShouldBeTrue();
        ai.HasKey.ShouldBeTrue();

        // Clearing the key switches cloud back off.
        await ai.SaveAsync(AiMode.Cloud, "https://api.openai.com/v1", "gpt-4o-mini", "");
        ai.IsEnabled.ShouldBeFalse();
        ai.HasKey.ShouldBeFalse();
    }

    [Fact]
    public async Task The_choice_survives_a_reload()
    {
        var state = new InMemoryUiState();
        var keyring = new InMemoryKeyring();
        var first = new AiOptions(state, keyring, new HttpClient());
        await first.SaveAsync(AiMode.Local, "http://localhost:11434/v1", "llama3", null);

        var second = new AiOptions(state, keyring, new HttpClient());

        second.Mode.ShouldBe(AiMode.Local);
        second.Endpoint.ShouldBe("http://localhost:11434/v1");
        second.Model.ShouldBe("llama3");
        second.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task A_disabled_service_returns_nothing()
    {
        var ai = new AiOptions(new InMemoryUiState(), new InMemoryKeyring(), new HttpClient());

        (
            await ai.SuggestAsync(new SuggestionRequest("anything", new DateOnly(2026, 10, 7)))
        ).ShouldBeNull();
    }

    [Fact]
    public void On_device_apple_intelligence_reports_why_it_cannot_run_yet()
    {
        var ai = new AiOptions(new InMemoryUiState(), new InMemoryKeyring(), new HttpClient());

        // The helper is not bundled, so the mode is offered but explained rather than silently dead.
        ai.Registry.ReasonUnavailable(AiMode.AppleOnDevice).ShouldNotBeNullOrWhiteSpace();
        ai.Registry.ReasonUnavailable(AiMode.Off).ShouldBeNull();
        ai.Registry.ReasonUnavailable(AiMode.Local).ShouldBeNull();
    }
}

// The AI pane has to re-read availability when it is opened. The helper is a file on disk that can
// appear while the app is running, and `AiChoice` alone won't raise a change when the mode is unchanged,
// which used to leave a stale "not installed" note after `mise run ai:helper`.
public sealed class AiSettingsPaneTests : IDisposable
{
    readonly AppFixture _app;

    public AiSettingsPaneTests()
    {
        _app = new AppFixture(
            ai: new AiOptions(new InMemoryUiState(), new InMemoryKeyring(), new HttpClient())
        );
    }

    public void Dispose() => _app.Dispose();

    SettingsViewModel Pane() => _app.Vms.Settings(_app.Workspace.Id, _app.Vms.Appearance(), null);

    [Fact]
    public async Task Reopening_the_pane_re_reads_ai_availability()
    {
        var pane = Pane();
        await pane.LoadAsync();

        var raised = 0;
        pane.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.AiModeReason))
                raised++;
        };

        await pane.LoadAsync();

        raised.ShouldBeGreaterThan(
            0,
            "reopening must re-check the helper, or installing it stays invisible"
        );
    }

    [Fact]
    public async Task Recheck_reports_readiness_or_the_reason()
    {
        var pane = Pane();
        await pane.LoadAsync();
        pane.AiChoice = pane.AiModes.Single(m => m.Mode == AiMode.AppleOnDevice);

        pane.RecheckAiCommand.Execute(null);

        // Either the helper is installed here (ready) or the pane explains what is missing. Never both,
        // and never a silent success.
        if (pane.AiModeReason is null)
            pane.AiStatus.ShouldBe("Ready to use.");
        else
            pane.AiStatus.ShouldBeNull();
    }

    [Fact]
    public async Task The_ai_pane_is_offered_with_the_global_switch()
    {
        var pane = Pane();
        await pane.LoadAsync();

        pane.HasAi.ShouldBeTrue();
        pane.AiModes.Select(m => m.Mode).ShouldContain(AiMode.Off);
        pane.AiModes.Select(m => m.Mode).ShouldContain(AiMode.AppleOnDevice);
    }
}

// An ISuggestionService whose reply, failure and enabled state are scripted. It is also observable, so
// the form's reaction to a settings change can be tested.
sealed class FakeSuggestionService : ISuggestionService, INotifyPropertyChanged
{
    public bool Enabled { get; set; }
    public FieldSuggestion? Result { get; set; }
    public Exception? Throw { get; set; }
    public int Calls { get; private set; }

    // The last request, so a test can assert what the model was actually handed.
    public SuggestionRequest? Last { get; private set; }

    public bool IsEnabled => Enabled;

    public Task<FieldSuggestion?> SuggestAsync(
        SuggestionRequest request,
        CancellationToken ct = default
    )
    {
        Calls++;
        Last = request;
        if (Throw is not null)
            throw Throw;
        return Task.FromResult(Result);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetEnabled(bool value)
    {
        Enabled = value;
        PropertyChanged?.Invoke(this, new(nameof(ISuggestionService.IsEnabled)));
    }
}

// Stands in for the preview stack: records which URLs were asked for, and returns a scripted answer.
sealed class FakeLinkResolver : ILinkResolver
{
    public LinkContext? Result { get; set; }
    public Exception? Throw { get; set; }
    public List<string> Asked { get; } = [];

    public Task<LinkContext?> ResolveAsync(string url, CancellationToken ct = default)
    {
        Asked.Add(url);
        if (Throw is not null)
            throw Throw;
        return Task.FromResult(Result);
    }
}
