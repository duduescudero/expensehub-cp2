using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace ExpenseHub.Reports;

internal sealed class RabbitPublisher : IReportPublisher, IAsyncDisposable
{
    internal const string Queue = "expensehub.reports";
    private readonly IConfiguration _configuration;
    private readonly ILogger<RabbitPublisher> _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitPublisher(IConfiguration configuration, ILogger<RabbitPublisher> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    internal static ConnectionFactory Factory(IConfiguration configuration) => new()
    {
        HostName = configuration["Rabbit:Host"] ?? "localhost",
        UserName = configuration["Rabbit:User"] ?? throw new InvalidOperationException("Configure Rabbit__User."),
        Password = configuration["Rabbit:Password"] ?? throw new InvalidOperationException("Configure Rabbit__Password."),
        RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
        ContinuationTimeout = TimeSpan.FromSeconds(5),
        AutomaticRecoveryEnabled = false,
    };

    public async Task PublishAsync(Guid id, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (_channel?.IsOpen != true)
            {
                if (_channel is not null)
                {
                    await _channel.DisposeAsync();
                }

                if (_connection is not null)
                {
                    await _connection.DisposeAsync();
                }

                _connection = await Factory(_configuration).CreateConnectionAsync(cancellationToken);
                _channel = await _connection.CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), cancellationToken);
                await _channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            byte[] body = JsonSerializer.SerializeToUtf8Bytes(new ReportMessage(id), JsonSerializerOptions.Web);
            await _channel.BasicPublishAsync(string.Empty, Queue, mandatory: true, new BasicProperties { Persistent = true, ContentType = "application/json", MessageId = id.ToString() }, body, timeout.Token);
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Publicação confirmada reportId={ReportId}", id);
            }
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _mutex.Dispose();
    }
}
