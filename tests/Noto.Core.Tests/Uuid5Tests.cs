using Noto.Core.Recurrence;

namespace Noto.Core.Tests;

public class Uuid5Tests
{
    [Fact]
    public void Matches_the_rfc_4122_reference_vector()
    {
        var dns = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
        Uuid5.Create(dns, "python.org").ShouldBe(Guid.Parse("886313e1-3b8a-5372-9b90-0c9aee199e5d"));
    }

    [Fact]
    public void Occurrence_ids_are_deterministic_and_date_specific()
    {
        var rule = Guid.CreateVersion7();
        var day = new DateOnly(2026, 10, 7);

        Uuid5.ForOccurrence(rule, day).ShouldBe(Uuid5.ForOccurrence(rule, day));
        Uuid5.ForOccurrence(rule, day).ShouldNotBe(Uuid5.ForOccurrence(rule, day.AddDays(1)));
    }
}
