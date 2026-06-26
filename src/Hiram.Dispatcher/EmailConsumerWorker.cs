using System.Diagnostics;
using System.Text;
using Hiram.Infrastructure.Messaging;
using Hiram.Infrastructure.Telemetry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Hiram.Dispatcher;

public sealed class EmailConsumerWorker : BackgroundService
{
    private static readonly TimeSpan RequeueBackoff = TimeSpan.FromSeconds(2);

    private readonly RabbitMqConnection _connection;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailConsumerWorker> _logger;
    private IChannel? _channel;

    public EmailConsumerWorker(
        RabbitMqConnection connection,
        IServiceScopeFactory scopeFactory,
        ILogger<EmailConsumerWorker> logger)
    {
        _connection = connection;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connection = await _connection.GetConnectionAsync(stoppingToken);
        _channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnReceivedAsync;

        await _channel.BasicConsumeAsync(HiramTopology.EmailQueue, autoAck: false, consumer, stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (_channel is not null)
                await _channel.DisposeAsync();
        }
    }

    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs args)
    {
        using var activity = HiramDiagnostics.Messaging.StartActivity(
            "consume email", ActivityKind.Consumer, ExtractContext(args.BasicProperties.Headers));

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<EmailNotificationProcessor>();
            await processor.ProcessAsync(args.Body, args.CancellationToken);

            await _channel!.BasicAckAsync(args.DeliveryTag, multiple: false, args.CancellationToken);
        }
        catch (PoisonMessageException ex)
        {
            // Deterministic poison (bad payload or a notification that cannot exist): park it for inspection
            // instead of dropping it, then ack so it leaves the work queue.
            _logger.LogWarning(ex, "Parking poison email message {DeliveryTag}", args.DeliveryTag);
            await ParkAsync(args, ex.Message);
            HiramDiagnostics.Poisoned.Add(1);
            await _channel!.BasicAckAsync(args.DeliveryTag, multiple: false, args.CancellationToken);
        }
        catch (Exception ex)
        {
            // Transient infrastructure failure (for example the database is unreachable): requeue so the broker
            // redelivers once the dependency recovers. A long outage spins this loop, bounded only by the short
            // backoff below, because scheduled retry with delay is deliberately out of scope for this slice.
            _logger.LogError(ex, "Transient failure on email message {DeliveryTag}, requeuing", args.DeliveryTag);
            await Task.Delay(RequeueBackoff, args.CancellationToken);
            await _channel!.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: true, args.CancellationToken);
        }
    }

    private async Task ParkAsync(BasicDeliverEventArgs args, string reason)
    {
        var headers = args.BasicProperties.Headers is { } existing
            ? new Dictionary<string, object?>(existing)
            : new Dictionary<string, object?>();
        headers["x-dead-letter-reason"] = reason;

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = args.BasicProperties.ContentType,
            Headers = headers
        };

        await _channel!.BasicPublishAsync(
            HiramTopology.Dlx, HiramTopology.DeadLetterRoutingKey, mandatory: false, properties, args.Body, args.CancellationToken);
    }

    private static ActivityContext ExtractContext(IDictionary<string, object?>? headers)
    {
        if (headers is not null
            && headers.TryGetValue("traceparent", out var raw)
            && raw is byte[] bytes
            && ActivityContext.TryParse(Encoding.UTF8.GetString(bytes), null, out var context))
        {
            return context;
        }

        return default;
    }
}
