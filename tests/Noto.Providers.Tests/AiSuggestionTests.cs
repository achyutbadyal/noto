using System.Net;
using System.Text.Json;
using Noto.Core.Ai;
using Noto.Core.Links;
using Noto.Providers.Ai;

namespace Noto.Providers.Tests;

// A link resolved before the model is asked changes what the model can do: it can write a title from
// what the page *is* instead of guessing from a bare URL. These pin that the resolved facts arrive, and
// that an unreadable link is disclosed rather than papered over.
public sealed class SuggestionPromptTests
{
    static readonly DateOnly Today = new(2026, 10, 7);

    [Fact]
    public void A_resolved_link_is_handed_over_as_fact()
    {
        var prompt = SuggestionPrompt.For(
            new SuggestionRequest(
                "review https://github.com/acme/api/pull/482",
                Today,
                new LinkContext(
                    "https://github.com/acme/api/pull/482",
                    "Fix login redirect",
                    "GitHub",
                    "Closes #471",
                    "Merged",
                    ["#482", "merged 2h ago"],
                    null
                )
            )
        );

        prompt.User.ShouldContain("https://github.com/acme/api/pull/482");
        prompt.User.ShouldContain("Fix login redirect");
        prompt.User.ShouldContain("GitHub");
        prompt.User.ShouldContain("Merged");
        prompt.User.ShouldContain("#482");
        prompt.User.ShouldContain("Closes #471");
        prompt.User.ShouldContain("most reliable signal");
    }

    [Fact]
    public void An_unreadable_link_is_disclosed_so_the_model_does_not_invent_it()
    {
        var prompt = SuggestionPrompt.For(
            new SuggestionRequest(
                "check https://example.com/private",
                Today,
                new LinkContext(
                    "https://example.com/private",
                    null,
                    "Web link",
                    null,
                    null,
                    [],
                    "needs a connected account"
                )
            )
        );

        prompt.User.ShouldContain("could not be read");
        prompt.User.ShouldContain("needs a connected account");
        prompt.User.ShouldContain("do not guess");
    }

    [Fact]
    public void No_link_means_no_link_block()
    {
        var prompt = SuggestionPrompt.For(new SuggestionRequest("call the dentist", Today));

        prompt.User.ShouldNotContain("links to");
        prompt.User.ShouldContain("call the dentist");
        prompt.User.ShouldContain("2026-10-07");
    }
}

// The model is an untrusted source, so the parser is where a bad reply stops being able to touch the
// form. These pin the clamping and the failure modes; the provider tests pin the wire shape.
public sealed class AiSuggestionParserTests
{
    static readonly DateOnly Today = new(2026, 10, 7);

    [Fact]
    public void Reads_a_plain_json_object()
    {
        var suggestion = SuggestionParser.Parse(
            """
            {"title":"Call the dentist","estimate_minutes":30,"priority":2,
             "planned_for":"2026-10-08","time_of_day":"morning","waiting_on":"Priya",
             "notes":"Ask about the crown","tags":["Health","health"],"confidence":0.8,
             "rationale":"The text names a day and a person."}
            """,
            Today
        );

        suggestion.Title.ShouldBe("Call the dentist");
        suggestion.EstimateMinutes.ShouldBe(30);
        suggestion.Priority.ShouldBe(2);
        suggestion.PlannedFor.ShouldBe(new DateOnly(2026, 10, 8));
        suggestion.TimeOfDay.ShouldBe(Noto.Core.Models.TimeOfDay.Morning);
        suggestion.WaitingOn.ShouldBe("Priya");
        suggestion.Notes.ShouldBe("Ask about the crown");
        suggestion.Tags.ShouldBe(["health"]); // lowercased and de-duplicated
        suggestion.Confidence.ShouldBe(0.8);
        suggestion.Rationale.ShouldNotBeNullOrWhiteSpace();
        suggestion.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void Survives_code_fences_and_surrounding_prose()
    {
        var suggestion = SuggestionParser.Parse(
            """
            Sure! Here is the JSON:
            ```json
            {"title":"Ship v2","estimate_minutes":90}
            ```
            Let me know if you want changes.
            """,
            Today
        );

        suggestion.Title.ShouldBe("Ship v2");
        suggestion.EstimateMinutes.ShouldBe(90);
    }

    [Fact]
    public void Clamps_values_that_are_out_of_range()
    {
        var suggestion = SuggestionParser.Parse(
            """{"priority":9,"estimate_minutes":-5,"confidence":4}""",
            Today
        );

        suggestion.Priority.ShouldBe(4); // clamped to the top of the range
        suggestion.EstimateMinutes.ShouldBe(1);
        suggestion.Confidence.ShouldBe(1);
    }

    [Fact]
    public void Drops_a_plan_in_the_past_and_unparseable_dates()
    {
        var suggestion = SuggestionParser.Parse(
            """{"planned_for":"2026-10-01","due_date":"whenever"}""",
            Today
        );

        suggestion.PlannedFor.ShouldBeNull();
        suggestion.DueDate.ShouldBeNull();
    }

    [Fact]
    public void Ignores_unknown_keys_and_unusable_values()
    {
        var suggestion = SuggestionParser.Parse(
            """{"title":"   ","time_of_day":"brunch","mystery":42,"tags":"nope"}""",
            Today
        );

        suggestion.Title.ShouldBeNull();
        suggestion.TimeOfDay.ShouldBeNull();
        suggestion.Tags.ShouldBeEmpty();
        suggestion.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Accepts_a_quoted_number()
    {
        var suggestion = SuggestionParser.Parse("""{"estimate_minutes":"45"}""", Today);

        suggestion.EstimateMinutes.ShouldBe(45);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("I could not understand that.")]
    public void Throws_when_there_is_no_json_to_use(string reply)
    {
        Should
            .Throw<AiSuggestionException>(() => SuggestionParser.Parse(reply, Today))
            .Message.ShouldContain("JSON");
    }

    [Fact]
    public void Throws_when_the_json_is_malformed()
    {
        Should.Throw<AiSuggestionException>(() => SuggestionParser.Parse("{\"title\":", Today));
    }
}

public sealed class AiProviderRegistryTests
{
    [Fact]
    public void Resolves_a_provider_for_each_usable_mode()
    {
        var registry = new AiProviderRegistry([
            new StubProvider(AiMode.Local, "{}"),
            new StubProvider(AiMode.Cloud, "{}"),
        ]);

        registry.For(AiMode.Local).ShouldNotBeNull();
        registry.For(AiMode.Cloud).ShouldNotBeNull();
        registry.For(AiMode.Off).ShouldBeNull();
    }

    [Fact]
    public void Skips_a_provider_that_cannot_run_here_and_explains_why()
    {
        var registry = new AiProviderRegistry([
            new StubProvider(AiMode.Cloud, "{}", unavailable: "No key was configured."),
        ]);

        registry.For(AiMode.Cloud).ShouldBeNull();
        registry.ReasonUnavailable(AiMode.Cloud).ShouldBe("No key was configured.");
        registry.ReasonUnavailable(AiMode.Off).ShouldBeNull();
    }
}

public sealed class AiSuggestionServiceTests
{
    static readonly DateOnly Today = new(2026, 10, 7);

    [Fact]
    public async Task Is_disabled_and_returns_nothing_when_the_mode_is_off()
    {
        var service = new AiSuggestionService(
            new AiProviderRegistry([new StubProvider(AiMode.Cloud, """{"title":"x"}""")]),
            new AiConnection(AiMode.Off, null, null, null)
        );

        service.IsEnabled.ShouldBeFalse();
        (await service.SuggestAsync(new SuggestionRequest("anything", Today))).ShouldBeNull();
    }

    [Fact]
    public async Task Returns_nothing_for_empty_input_without_calling_the_model()
    {
        var provider = new StubProvider(AiMode.Cloud, """{"title":"x"}""");
        var service = new AiSuggestionService(
            new AiProviderRegistry([provider]),
            new AiConnection(AiMode.Cloud, "https://api.test/v1", "m", "k")
        );

        (await service.SuggestAsync(new SuggestionRequest("   ", Today))).ShouldBeNull();
        provider.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Builds_the_prompt_calls_the_provider_and_parses_the_reply()
    {
        var provider = new StubProvider(
            AiMode.Cloud,
            """{"title":"Call the dentist","planned_for":"2026-10-08"}"""
        );
        var service = new AiSuggestionService(
            new AiProviderRegistry([provider]),
            new AiConnection(AiMode.Cloud, "https://api.test/v1", "m", "k")
        );

        var suggestion = await service.SuggestAsync(
            new SuggestionRequest("call dentist tomorrow", Today)
        );

        service.IsEnabled.ShouldBeTrue();
        suggestion!.Title.ShouldBe("Call the dentist");
        suggestion.PlannedFor.ShouldBe(new DateOnly(2026, 10, 8));
        provider.LastPrompt!.User.ShouldContain("2026-10-07"); // today is passed to the model
        provider.LastPrompt.User.ShouldContain("call dentist tomorrow");
    }

    [Fact]
    public async Task Is_disabled_when_the_only_provider_cannot_run()
    {
        var service = new AiSuggestionService(
            new AiProviderRegistry([new StubProvider(AiMode.Local, "{}", unavailable: "nope")]),
            new AiConnection(AiMode.Local, "http://localhost:11434/v1", "m", null)
        );

        service.IsEnabled.ShouldBeFalse();
        (await service.SuggestAsync(new SuggestionRequest("x", Today))).ShouldBeNull();
    }
}

public sealed class OpenAiCompatibleProviderTests
{
    static readonly AiConnection Cloud = new(
        AiMode.Cloud,
        "https://api.example.com/v1",
        "test-model",
        "secret-key"
    );

    static readonly AiPrompt Prompt = new("system text", "user text");

    [Fact]
    public async Task Posts_a_chat_completion_and_returns_the_message()
    {
        var handler = new FakeHandler(
            (_, _) =>
                FakeHandler.Json(
                    """
                    {"choices":[{"message":{"content":"{\"title\":\"Call the dentist\"}"}}]}
                    """
                )
        );
        var provider = new OpenAiCompatibleProvider(new HttpClient(handler));

        var reply = await provider.CompleteAsync(Cloud, Prompt, CancellationToken.None);

        reply.ShouldBe("""{"title":"Call the dentist"}""");

        var (request, body) = handler.Calls.Single();
        request.RequestUri!.AbsoluteUri.ShouldBe("https://api.example.com/v1/chat/completions");
        request.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        request.Headers.Authorization.Parameter.ShouldBe("secret-key");

        using var sent = JsonDocument.Parse(body);
        sent.RootElement.GetProperty("model").GetString().ShouldBe("test-model");
        sent.RootElement.GetProperty("messages").GetArrayLength().ShouldBe(2);
        sent.RootElement.GetProperty("messages")[0]
            .GetProperty("content")
            .GetString()
            .ShouldBe("system text");
    }

    [Fact]
    public async Task Sends_no_authorization_header_when_there_is_no_key()
    {
        var handler = new FakeHandler(
            (_, _) => FakeHandler.Json("""{"choices":[{"message":{"content":"{}"}}]}""")
        );
        var provider = new OpenAiCompatibleProvider(new HttpClient(handler));
        var local = new AiConnection(AiMode.Local, "http://localhost:11434/v1", "llama3", null);

        await provider.CompleteAsync(local, Prompt, CancellationToken.None);

        var (request, _) = handler.Calls.Single();
        request.Headers.Authorization.ShouldBeNull();
        request.RequestUri!.AbsoluteUri.ShouldBe("http://localhost:11434/v1/chat/completions");
    }

    [Fact]
    public async Task Falls_back_to_the_default_endpoint_for_the_mode()
    {
        var handler = new FakeHandler(
            (_, _) => FakeHandler.Json("""{"choices":[{"message":{"content":"{}"}}]}""")
        );
        var provider = new OpenAiCompatibleProvider(new HttpClient(handler));

        await provider.CompleteAsync(
            new AiConnection(AiMode.Local, null, "llama3", null),
            Prompt,
            CancellationToken.None
        );

        handler
            .Calls.Single()
            .Request.RequestUri!.AbsoluteUri.ShouldStartWith("http://localhost:11434");
    }

    [Fact]
    public async Task Reads_a_multi_part_content_reply()
    {
        var handler = new FakeHandler(
            (_, _) =>
                FakeHandler.Json(
                    """{"choices":[{"message":{"content":[{"type":"text","text":"{\"title\":\"A\"}"}]}}]}"""
                )
        );
        var provider = new OpenAiCompatibleProvider(new HttpClient(handler));

        var reply = await provider.CompleteAsync(Cloud, Prompt, CancellationToken.None);

        reply.ShouldBe("""{"title":"A"}""");
    }

    [Fact]
    public async Task Surfaces_the_providers_own_error_message()
    {
        var handler = new FakeHandler(
            (_, _) =>
                FakeHandler.Json(
                    """{"error":{"message":"Incorrect API key provided."}}""",
                    status: 401
                )
        );
        var provider = new OpenAiCompatibleProvider(new HttpClient(handler));

        var error = await Should.ThrowAsync<AiSuggestionException>(() =>
            provider.CompleteAsync(Cloud, Prompt, CancellationToken.None)
        );

        error.Message.ShouldBe("Incorrect API key provided.");
    }

    [Fact]
    public async Task Explains_an_unreachable_endpoint()
    {
        var handler = new FakeHandler((_, _) => throw new HttpRequestException("no route"));
        var provider = new OpenAiCompatibleProvider(new HttpClient(handler));

        var error = await Should.ThrowAsync<AiSuggestionException>(() =>
            provider.CompleteAsync(Cloud, Prompt, CancellationToken.None)
        );

        error.Message.ShouldContain("api.example.com");
    }

    [Fact]
    public async Task Refuses_to_call_without_a_model()
    {
        var provider = new OpenAiCompatibleProvider(
            new HttpClient(
                new FakeHandler((_, _) => throw new InvalidOperationException("must not be called"))
            )
        );

        var error = await Should.ThrowAsync<AiSuggestionException>(() =>
            provider.CompleteAsync(
                new AiConnection(AiMode.Cloud, "https://api.example.com/v1", null, "k"),
                Prompt,
                CancellationToken.None
            )
        );

        error.Message.ShouldContain("model");
    }
}

// The C#↔Swift bridge contract. The Apple helper is a separate Swift binary (tools/noto-ai-helper), so a
// rename on either side would otherwise break it silently at runtime, with no compiler to catch it.
public sealed class AppleOnDeviceBridgeTests
{
    [Fact]
    public void The_request_carries_exactly_the_keys_the_swift_helper_decodes()
    {
        var json = AppleOnDeviceProvider.RequestJson(
            new AiPrompt("the instructions", "the raw text")
        );

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("system").GetString().ShouldBe("the instructions");
        document.RootElement.GetProperty("user").GetString().ShouldBe("the raw text");
        document.RootElement.EnumerateObject().Count().ShouldBe(2);
    }
}

// A provider whose reply is scripted, so service-level behavior can be tested without the wire.
sealed class StubProvider(AiMode mode, string reply, string? unavailable = null) : IAiProvider
{
    public int Calls { get; private set; }
    public AiPrompt? LastPrompt { get; private set; }

    public IReadOnlyList<AiMode> Modes => [mode];
    public string DisplayName => "stub";
    public string? UnavailableReason => unavailable;

    public Task<string> CompleteAsync(
        AiConnection connection,
        AiPrompt prompt,
        CancellationToken ct
    )
    {
        Calls++;
        LastPrompt = prompt;
        return Task.FromResult(reply);
    }
}
