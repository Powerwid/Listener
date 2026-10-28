using Listener.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Listener.Api.Data.Configurations;

public sealed class BbvaPaymentBalanceConfiguration : IEntityTypeConfiguration<BbvaPaymentBalance>
{
    public void Configure(EntityTypeBuilder<BbvaPaymentBalance> b)
    {
        b.ToTable("BbvaPaymentBalances", t => t.HasCheckConstraint("CK_BbvaBalance_Pending", "[PendingAmount] >= 0 AND [PendingAmount] < 10"));
        b.HasKey(x => x.PayerKey);
        b.Property(x => x.PayerKey).HasMaxLength(64).IsUnicode(false);
        b.Property(x => x.PendingAmount).HasPrecision(18, 2);
        b.Property(x => x.VersionFila).IsRowVersion();
    }
}

public sealed class BbvaPaymentValidationConfiguration : IEntityTypeConfiguration<BbvaPaymentValidation>
{
    public void Configure(EntityTypeBuilder<BbvaPaymentValidation> b)
    {
        b.ToTable("BbvaPaymentValidations", t =>
        {
            t.HasCheckConstraint("CK_BbvaValidation_Amounts", "[ReceivedAmount] > 0 AND [AccumulatedAmount] >= [ReceivedAmount]");
            t.HasCheckConstraint("CK_BbvaValidation_Correct", "([Correct] = 1 AND [AccumulatedAmount] >= 10) OR ([Correct] = 0 AND [AccumulatedAmount] < 10)");
        });
        b.HasKey(x => x.EventId);
        b.Property(x => x.EventId).ValueGeneratedNever();
        b.Property(x => x.PayerKey).HasMaxLength(64).IsUnicode(false);
        b.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        b.Property(x => x.ReceivedAmount).HasPrecision(18, 2);
        b.Property(x => x.AccumulatedAmount).HasPrecision(20, 2);
        b.HasIndex(x => new { x.PayerKey, x.Sequence }).IsUnique();
        b.HasOne<CorreoProcesado>().WithOne().HasForeignKey<BbvaPaymentValidation>(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BbvaPaymentBalance>().WithMany().HasForeignKey(x => x.PayerKey).OnDelete(DeleteBehavior.Restrict);
    }
}
