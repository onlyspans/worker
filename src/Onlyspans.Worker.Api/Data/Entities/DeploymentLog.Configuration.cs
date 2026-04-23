using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Onlyspans.Worker.Api.Data.Entities;

public class DeploymentLogConfiguration : IEntityTypeConfiguration<DeploymentLog>
{
    public void Configure(EntityTypeBuilder<DeploymentLog> builder)
    {
        builder.HasKey(dl => dl.Id);

        builder.Property(dl => dl.DeploymentId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(dl => dl.Timestamp)
            .IsRequired();

        builder.Property(dl => dl.LogLevel)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(dl => dl.Message)
            .IsRequired();

        builder.Property(dl => dl.Source)
            .HasMaxLength(256);

        
        builder.HasIndex(dl => dl.DeploymentId);
        builder.HasIndex(dl => dl.Timestamp);
        builder.HasIndex(dl => new { dl.DeploymentId, dl.Timestamp });
    }
}
