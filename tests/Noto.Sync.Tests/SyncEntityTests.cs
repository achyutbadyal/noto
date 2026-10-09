using Noto.Core.Sync;
using Shouldly;

namespace Noto.Sync.Tests;

public class SyncEntityTests
{
    [Fact]
    public void Every_entity_type_constant_is_registered()
    {
        var constants = typeof(EntityTypes)
            .GetFields()
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        EntityTypes.All.OrderBy(t => t).ShouldBe(constants.OrderBy(t => t));
    }

    [Fact]
    public void Every_entity_has_fields_and_a_codec()
    {
        foreach (var e in SyncEntities.All)
        {
            e.Fields.ShouldNotBeEmpty(e.Type);
            e.Codec.EntityType.ShouldBe(e.Type);
            SyncRows.FieldsOf(e.Type).ShouldBeSameAs(e.Fields);
        }
    }
}
