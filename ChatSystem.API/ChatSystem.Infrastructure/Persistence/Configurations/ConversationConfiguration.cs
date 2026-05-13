using ChatSystem.Domain.Entities;
using ChatSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatSystem.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the Conversation domain entity to the [Conversations] SQL Server table.
///
/// Key decisions:
/// - ConversationType enum is stored as TINYINT (byte) — maps directly to the
///   enum's underlying byte type. No string conversion needed.
/// - LastMessageAt is nullable — it is null until the first message is sent.
/// - The IX_Conversations_LastMessageAt index is non-clustered and descending
///   to match the "ORDER BY LastMessageAt DESC" inbox query exactly.
/// </summary>
public sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        // ── Table ─────────────────────────────────────────────────────────────
        builder.ToTable("Conversations");

        // ── Primary key ───────────────────────────────────────────────────────
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        // ── Columns ───────────────────────────────────────────────────────────
        builder.Property(c => c.Type)
            .IsRequired()
            .HasColumnType("tinyint")
            .HasConversion<byte>(); // Stores enum as numeric byte — not string

        builder.Property(c => c.Title)
            .IsRequired(false)
            .HasMaxLength(200)
            .HasColumnType("nvarchar(200)");

        builder.Property(c => c.CreatedByUserId)
            .IsRequired()
            .HasColumnType("uniqueidentifier");

        builder.Property(c => c.CreatedAt)
            .IsRequired()
            .HasColumnType("datetime2(7)");

        builder.Property(c => c.LastMessageAt)
            .IsRequired(false)
            .HasColumnType("datetime2(7)");

        // ── Foreign key: CreatedByUserId → Users.Id ───────────────────────────
        // No navigation property on Conversation — the FK is declared here
        // without a corresponding navigation. EF Core supports shadow FKs.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict); // Restrict: don't cascade-delete conversations when a user is deleted

        // ── Indexes ───────────────────────────────────────────────────────────

        builder.HasIndex(c => c.LastMessageAt)
            .IsDescending(true)
            .HasDatabaseName("IX_Conversations_LastMessageAt");
    }
}