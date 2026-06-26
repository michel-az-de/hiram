using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Hiram.Infrastructure.Messaging;

public sealed class RabbitMqConnection : IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<RabbitMqConnection> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public RabbitMqConnection(string connectionString, ILogger<RabbitMqConnection> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
            return _connection;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
                return _connection;

            _connection = await ConnectWithBackoffAsync(cancellationToken);
            await DeclareTopologyAsync(_connection, cancellationToken);
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IConnection> ConnectWithBackoffAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory { Uri = new Uri(_connectionString) };
        var delay = TimeSpan.FromSeconds(1);
        var maxDelay = TimeSpan.FromSeconds(10);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await factory.CreateConnectionAsync(cancellationToken);
            }
            catch (BrokerUnreachableException ex)
            {
                _logger.LogWarning(ex, "RabbitMQ not reachable yet, retrying in {DelaySeconds}s", delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromSeconds(Math.Min(maxDelay.TotalSeconds, delay.TotalSeconds * 2));
            }
        }
    }

    private static async Task DeclareTopologyAsync(IConnection connection, CancellationToken cancellationToken)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(HiramTopology.Exchange, ExchangeType.Direct, durable: true, autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(HiramTopology.EmailQueue, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(HiramTopology.EmailQueue, HiramTopology.Exchange, HiramTopology.EmailRoutingKey,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(HiramTopology.PushQueue, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(HiramTopology.PushQueue, HiramTopology.Exchange, HiramTopology.PushRoutingKey,
            cancellationToken: cancellationToken);

        // Parking lot for poison messages the consumer cannot ever process. Declared additively so the existing
        // work queue keeps its arguments and no second declare hits a precondition mismatch.
        await channel.ExchangeDeclareAsync(HiramTopology.Dlx, ExchangeType.Direct, durable: true, autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(HiramTopology.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(HiramTopology.DeadLetterQueue, HiramTopology.Dlx, HiramTopology.DeadLetterRoutingKey,
            cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();

        _gate.Dispose();
    }
}
