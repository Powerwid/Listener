using Listener.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Listener.Tests;

// SQLite exercises the production queries and transactions without touching the configured SQL Server.
// Only SQL Server DDL and timestamp storage are adapted; the entity relationships and query code stay the same.
public sealed class HistoryTestModel(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder builder, DbContext context)
    {
        base.Customize(builder, context);
        if (context is not ListenerDbContext) return;
        foreach (var entity in builder.Model.GetEntityTypes())
        {
            foreach (var constraint in entity.GetCheckConstraints().ToArray()) entity.RemoveCheckConstraint(constraint.Name!);
            foreach (var property in entity.GetProperties())
            {
                if (property.Name == "VersionFila")
                {
                    property.ValueGenerated = ValueGenerated.Never;
                    property.IsConcurrencyToken = false;
                }
                if (property.ClrType == typeof(DateTimeOffset))
                    builder.Entity(entity.ClrType).Property<DateTimeOffset>(property.Name)
                        .HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
                if (property.ClrType == typeof(DateTimeOffset?))
                    builder.Entity(entity.ClrType).Property<DateTimeOffset?>(property.Name)
                        .HasConversion(v => v.HasValue ? v.Value.UtcTicks : (long?)null,
                            v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);
            }
        }
    }
}
