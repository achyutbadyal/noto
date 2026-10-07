using UUIDNext;

namespace Noto.Core.Recurrence;

// Deterministic ids so two devices generating the same row produce the same id.
public static class Uuid5
{
    public static Guid ForOccurrence(Guid ruleId, DateOnly occurrence) =>
        Create(ruleId, occurrence.ToString("yyyy-MM-dd"));

    public static Guid Create(Guid ns, string name) => Uuid.NewNameBased(ns, name);
}
