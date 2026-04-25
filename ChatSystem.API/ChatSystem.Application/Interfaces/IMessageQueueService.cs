using ChatSystem.Application.DTOs;

namespace ChatSystem.Application.Interfaces;

/// <summary>
/// Abstraction over a message broker (RabbitMQ / Azure Service Bus in production).
///
/// WHY a queue at all?
/// After a message is persisted to SQL Server, the system must fan it out to:
///   1. The recipient's active SignalR connection(s)
///   2. Push notification service (if recipient is offline)
///   3. Any future consumers (analytics, moderation, search indexing)
///
/// Publishing to a queue decouples ChatService from all those consumers.
/// If a consumer is slow or down, the message is not lost — it waits in the queue.
/// This is the difference between at-most-once and at-least-once delivery.
/// </summary>
public interface IMessageQueueService
{
   
    Task PublishMessageAsync(
        MessageResponseDto message,
        CancellationToken cancellationToken = default);

    // Publishes a delivery status update event (Delivered or Read).
    // SignalR consumers use this to push status ticks to the sender's client.
    Task PublishStatusUpdateAsync(
        Guid messageId,
        string newStatus,
        CancellationToken cancellationToken = default);
}