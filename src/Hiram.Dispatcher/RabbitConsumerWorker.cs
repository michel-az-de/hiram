using System.Diagnostics;
using System.Text;
using Hiram.Infrastructure.Messaging;
using Hiram.Infrastructure.Telemetry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Hiram.Dispatcher;

// One channel consumer per queue. Acks on success, parks poison messages in the dead letter exchange,
// and requeues transient failures. On shutdown it stops taking new deliveries and drains in-flight
// handlers before the channel closes, so a deploy does not cut a send mid-flight.
public abstract class RabbitConsumerWorker : BackgroundService
{
    private static readonly TimeSpan RequeueBackoff = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DrainGrace = TimeSpan.FromSeconds(30);

    private readonly RabbitMqConnection _connection;
    private readonly ILogger _logger;
    private IChannel? _channel;
    private string? _consumerTag;
    private int _inFlight;

    protected RabbitConsumerWorker(RabbitMqConnection connection, ILogger logger)
    {
        _connection = connection;
        _logger = logger;
    }

    protected abstract string QueueName { get; }
    protected abstract string ActivityName { get; }
    protected abstract Task ProcessAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connection = await _connection.GetConnectionAsync(stoppingToken);
        _channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnReceivedAsync;

        _consumerTag = await _channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, stoppingToken);

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

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop new deliveries, then let in-flight handlers finish and ack before the channel closes. On
        // timeout we fall back to at-least-once: unacked messages are redelivered, never lost.
        if (_channel is { IsOpen: true } && _consumerTag is not null)
        {
            try
            {
                await _channel.BasicCancelAsync(_consumerTag, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                // Cancel is best effort; proceed with the drain and stop so shutdown still completes.
                _logger.LogWarning(ex, "Failed to cancel consumer {ConsumerTag} during drain", _consumerTag);
            }
        }

        await ConsumerDrain.WaitAsync(
            () => Volatile.Read(ref _inFlight), DrainGrace, TimeSpan.FromMilliseconds(100), cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs args)
    {
        Interlocked.Increment(ref _inFlight);
        using var activity = HiramDiagnostics.Messaging.StartActivity(
            ActivityName, ActivityKind.Consumer, ExtractContext(args.BasicProperties.Headers));

        try
        {
            await ProcessAsync(args.Body, args.CancellationToken);
            await _channel!.BasicAckAsync(args.DeliveryTag, multiple: false, args.CancellationToken);
        }
        catch (PoisonMessageException ex)
        {
            // Deterministic poison (bad payload or a notification that cannot exist): park it for
            // inspection instead of dropping it, then ack so it leaves the work queue.
            _logger.LogWarning(ex, "Parking poison message {DeliveryTag} from {Queue}", args.DeliveryTag, QueueName);
            await ParkAsync(args, ex.Message);
            HiramDiagnostics.Poisoned.Add(1);
            await _channel!.BasicAckAsync(args.DeliveryTag, multiple: false, args.CancellationToken);
        }
        catch (Exception ex)
        {
            // Transient infrastructure failure (for example the database is unreachable): requeue so the
            // broker redelivers once the dependency recovers, bounded by the short backoff below.
            _logger.LogError(ex, "Transient failure on message {DeliveryTag} from {Queue}, requeuing", args.DeliveryTag, QueueName);
            await Task.Delay(RequeueBackoff, args.CancellationToken);
            await _channel!.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: true, args.CancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
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
