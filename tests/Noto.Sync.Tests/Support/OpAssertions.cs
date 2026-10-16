using System.Text.Json.Nodes;
using Noto.Core.Sync;

namespace Noto.Sync.Tests.Support;

// Every recorded op must name a known entity type and only fields that entity is allowed to sync. Both the
// command path (OpCaptureTests) and the service path (ServiceCaptureTests) assert this, over their own
// writers, so the check lives here once instead of as two copies free to drift apart.
static class OpAssertions
{
    public static void NameOnlyKnownEntitiesAndFields(IEnumerable<Op> ops)
    {
        foreach (var op in ops)
        {
            EntityTypes.All.ShouldContain(op.EntityType);
            var allowed = SyncRows.FieldsOf(op.EntityType)!;
            if (op.Kind == OpKinds.Set)
                allowed.ShouldContain(op.Field!);
            else
                ((JsonObject)op.Value!)
                    .Select(kv => kv.Key)
                    .Where(k => k != "id")
                    .ShouldAllBe(k => allowed.Contains(k));
        }
    }
}
