using ChatSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatSystem.Infrastructure.Persistence.Configurations;

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        // ── Table ─────────────────────────────────────────────────────────────
        builder.ToTable("Messages");

        // ── Primary key ───────────────────────────────────────────────────────
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        // ── Columns ───────────────────────────────────────────────────────────
        builder.Property(m => m.ConversationId)
            .IsRequired()
            .HasColumnType("uniqueidentifier");

        builder.Property(m => m.SenderId)
            .IsRequired()
            .HasColumnType("uniqueidentifier");

        builder.Property(m => m.Body)
            .IsRequired()
            .HasMaxLength(4000)
            .HasColumnType("nvarchar(4000)");

        builder.Property(m => m.Status)
            .IsRequired()
            .HasColumnType("tinyint")
            .HasConversion<byte>();

        builder.Property(m => m.SentAt)
            .IsRequired()
            .HasColumnType("datetime2(7)");

        builder.Property(m => m.DeliveredAt)
            .IsRequired(false)
            .HasColumnType("datetime2(7)");

        builder.Property(m => m.ReadAt)
            .IsRequired(false)
            .HasColumnType("datetime2(7)");

        builder.Property(m => m.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        // ── Global query filter — soft delete ─────────────────────────────────
        // Every LINQ query against Messages automatically appends
        // "WHERE IsDeleted = 0" unless the caller explicitly calls
        // .IgnoreQueryFilters(). This prevents deleted messages from leaking
        // into any query across the entire codebase without extra effort.
        builder.HasQueryFilter(m => !m.IsDeleted);

        // ── Foreign keys ──────────────────────────────────────────────────────
        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade); // Deleting a conversation removes its messages

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(m => m.SenderId)
            .OnDelete(DeleteBehavior.Restrict); // Don't cascade-delete messages when user is deleted

        // ── Indexes ───────────────────────────────────────────────────────────

        // PRIMARY performance index — serves "load chat history" paginated query.
        // Composite: ConversationId (filter) + SentAt DESC (sort + cursor).
        // INCLUDE makes this a covering index — no key lookup to main table needed.
        builder.HasIndex(m => new { m.ConversationId, m.SentAt })
            .IsDescending(false, true) // ConversationId ASC, SentAt DESC
            .HasDatabaseName("IX_Messages_ConversationId_SentAt");

        // Supports the reconnect sweep: find undelivered messages for a user.
        // Status is low-cardinality but combined with SenderId narrows results fast.
        builder.HasIndex(m => new { m.SenderId, m.Status })
            .HasDatabaseName("IX_Messages_SenderId_Status");
    }
}