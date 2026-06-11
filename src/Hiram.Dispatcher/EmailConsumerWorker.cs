using Hiram.Infrastructure.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Hiram.Dispatcher;

public sealed class EmailConsumerWorker : BackgroundService
{
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
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<EmailNotificationProcessor>();
            await processor.ProcessAsync(args.Body, args.CancellationToken);

            await _channel!.BasicAckAsync(args.DeliveryTag, multiple: false, args.CancellationToken);
        }
        catch (Exception ex)
        {
            // Reject without requeue so a poison message does not spin forever; replay and DLQ arrive in F2.
            _logger.LogError(ex, "Failed to process email message {DeliveryTag}", args.DeliveryTag);
            await _channel!.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, args.CancellationToken);
        }
    }
}
