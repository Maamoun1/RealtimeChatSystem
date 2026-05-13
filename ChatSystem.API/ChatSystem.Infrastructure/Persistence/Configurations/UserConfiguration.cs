using ChatSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatSystem.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        // ── Table ─────────────────────────────────────────────────────────────
        builder.ToTable("Users");

        // ── Primary key ───────────────────────────────────────────────────────
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever(); // Application (domain) generates the GUID, not the DB

        // ── Columns ───────────────────────────────────────────────────────────
        builder.Property(u => u.Username)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnType("nvarchar(50)");

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(255)
            .HasColumnType("nvarchar(255)");

        builder.Property(u => u.PasswordHash)
            .IsRequired()
            .HasMaxLength(512)
            .HasColumnType("nvarchar(512)");

        builder.Property(u => u.DisplayName)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnType("nvarchar(100)");

        builder.Property(u => u.AvatarUrl)
            .IsRequired(false)
            .HasMaxLength(500)
            .HasColumnType("nvarchar(500)");

        builder.Property(u => u.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(u => u.CreatedAt)
            .IsRequired()
            .HasColumnType("datetime2(7)");

        builder.Property(u => u.UpdatedAt)
            .IsRequired()
            .HasColumnType("datetime2(7)");

        // ── Unique constraints ────────────────────────────────────────────────
        // These enforce uniqueness at the DB level — a safety net beyond app logic.
        builder.HasIndex(u => u.Username)
            .IsUnique()
            .HasDatabaseName("UQ_Users_Username");

        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasDatabaseName("UQ_Users_Email");

    }
}