using ChatSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatSystem.Infrastructure.Persistence.Configurations;


public sealed class ConversationParticipantConfiguration
    : IEntityTypeConfiguration<ConversationParticipant>
{
    public void Configure(EntityTypeBuilder<ConversationParticipant> builder)
    {
        // ── Table ─────────────────────────────────────────────────────────────
        builder.ToTable("ConversationParticipants");

        // ── Primary key ───────────────────────────────────────────────────────
        builder.HasKey(cp => cp.Id);

        builder.Property(cp => cp.Id)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        // ── Columns ───────────────────────────────────────────────────────────
        builder.Property(cp => cp.ConversationId)
            .IsRequired()
            .HasColumnType("uniqueidentifier");

        builder.Property(cp => cp.UserId)
            .IsRequired()
            .HasColumnType("uniqueidentifier");

        builder.Property(cp => cp.JoinedAt)
            .IsRequired()
            .HasColumnType("datetime2(7)");

        builder.Property(cp => cp.LastReadAt)
            .IsRequired(false)
            .HasColumnType("datetime2(7)");

        builder.Property(cp => cp.IsAdmin)
            .IsRequired()
            .HasDefaultValue(false);

        // ── Unique constraint — no duplicate memberships ───────────────────────
        builder.HasIndex(cp => new { cp.ConversationId, cp.UserId })
            .IsUnique()
            .HasDatabaseName("UQ_ConversationParticipants_ConversationId_UserId");

        // ── Foreign keys ──────────────────────────────────────────────────────
        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(cp => cp.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(cp => cp.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Indexes ───────────────────────────────────────────────────────────

        // "What conversations is user X in?" — inbox load on app open.
        // INCLUDE LastReadAt avoids a key lookup for the unread count calculation.
        builder.HasIndex(cp => new { cp.UserId, cp.ConversationId })
            .HasDatabaseName("IX_CP_UserId_ConversationId")
            .IncludeProperties(cp => new { cp.LastReadAt, cp.JoinedAt });

        // "Who are the members of conversation X?" — participant list load.
        builder.HasIndex(cp => new { cp.ConversationId, cp.UserId })
            .HasDatabaseName("IX_CP_ConversationId_UserId");
    }
}