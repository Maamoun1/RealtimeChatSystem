using ChatSystem.Application.DTOs;
using ChatSystem.Application.Interfaces;
using Microsoft.EntityFrameworkCore.Metadata;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace ChatSystem.Infrastructure.Messaging;


public sealed class RabbitMqMessageQueueService : IMessageQueueService, IDisposable
{
    private const string ExchangeName = "chat.events";
    private const string MessageSentQueue = "chat.messages.sent";
    private const string MessageStatusQueue = "chat.messages.status";
    private const string RoutingKeySent = "message.sent";
    private const string RoutingKeyStatus = "message.status";

    private readonly IConnection _connection;
    private readonly RabbitMQ.Client.IModel _channel;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RabbitMqMessageQueueService(IConnection connection)
    {
        _connection = connection;
        _channel = _connection.CreateModel();

        InitialiseTopology();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Topology setup — idempotent, safe to call on every startup
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Declares the exchange and queues. RabbitMQ's ExchangeDeclare and
    /// QueueDeclare are idempotent — if the topology already exists with the
    /// same settings, this is a no-op. If settings differ, an exception is thrown
    /// (intentional — mismatched topology is a deployment error, not a runtime error).
    /// </summary>
    private void InitialiseTopology()
    {
        // Durable direct exchange — survives broker restart
        _channel.ExchangeDeclare(
            exchange: ExchangeName,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false);

        // Queue for new messages
        _channel.QueueDeclare(
            queue: MessageSentQueue,
            durable: true,   // survives restart
            exclusive: false,  // multiple consumers allowed
            autoDelete: false);

        _channel.QueueBind(
            queue: MessageSentQueue,
            exchange: ExchangeName,
            routingKey: RoutingKeySent);

        // Queue for status updates
        _channel.QueueDeclare(
            queue: MessageStatusQueue,
            durable: true,
            exclusive: false,
            autoDelete: false);

        _channel.QueueBind(
            queue: MessageStatusQueue,
            exchange: ExchangeName,
            routingKey: RoutingKeyStatus);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Publish: new message
    // ─────────────────────────────────────────────────────────────────────────

    public Task PublishMessageAsync(
        MessageResponseDto message,
        CancellationToken cancellationToken = default)
    {
        // Map the Application DTO to the infrastructure event contract.
        // This keeps the queue payload decoupled from the API response shape.
        var @event = new MessageSentEvent(
            MessageId: message.Id,
            ConversationId: message.ConversationId,
            SenderId: message.SenderId,
            SenderName: message.SenderName,
            Body: message.Body,
            Status: message.Status,
            SentAt: message.SentAt);

        return PublishAsync(@event, RoutingKeySent, cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Publish: status update
    // ─────────────────────────────────────────────────────────────────────────

    public Task PublishStatusUpdateAsync(
        Guid messageId,
        string newStatus,
        CancellationToken cancellationToken = default)
    {
        var @event = new MessageStatusUpdatedEvent(
            MessageId: messageId,
            NewStatus: newStatus,
            UpdatedAt: DateTime.UtcNow);

        return PublishAsync(@event, RoutingKeyStatus, cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Internal publish helper
    // ─────────────────────────────────────────────────────────────────────────

    private Task PublishAsync<T>(
        T @event,
        string routingKey,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(@event, JsonOptions);
        var body = Encoding.UTF8.GetBytes(json);

        var props = _channel.CreateBasicProperties();
        props.ContentType = "application/json";
        props.DeliveryMode = 2;          // 2 = Persistent: written to disk before ACK
        props.MessageId = Guid.NewGuid().ToString(); // Unique ID for deduplication

        // BasicPublish is synchronous in the RabbitMQ .NET client.
        // For truly async publishing use IAsyncChannel (RabbitMQ.Client 7+)
        // or wrap in Task.Run — here we return a completed Task for interface compliance.
        _channel.BasicPublish(
            exchange: ExchangeName,
            routingKey: routingKey,
            basicProperties: props,
            body: body);

        return Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Dispose
    // ─────────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_channel.IsOpen) _channel.Close();
        if (_connection.IsOpen) _connection.Close();
    }
}