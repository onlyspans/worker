using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Onlyspans.Worker.Api.Data.Entities;

public class DeploymentResultConfiguration : IEntityTypeConfiguration<DeploymentResult>
{
    public void Configure(EntityTypeBuilder<DeploymentResult> builder)
    {
        builder.HasKey(dr => dr.DeploymentId);

        builder.Property(dr => dr.DeploymentId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(dr => dr.Status)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(dr => dr.StartedAt)
            .IsRequired();

        builder.Property(dr => dr.CompletedAt);

        builder.Property(dr => dr.ErrorMessage);

        // Index for status queries
        builder.HasIndex(dr => dr.Status);
        builder.HasIndex(dr => dr.StartedAt);
    }
}
