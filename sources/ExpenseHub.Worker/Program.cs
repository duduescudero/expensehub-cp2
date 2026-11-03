using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Data;
using ExpenseHub.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace ExpenseHub.Worker;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddDbContext<ExpenseDbContext>(options => options.UseSqlite(builder.Configuration.GetConnectionString("ExpenseHub") ?? "Data Source=expensehub.db;Default Timeout=30"));
        builder.Services.AddScoped<IReportStore, ReportStore>();
        builder.Services.AddScoped<ReportProcessor>();
        builder.Services.AddHostedService<ReportsWorker>();
        await builder.Build().RunAsync();
    }
}

internal sealed class ReportsWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ReportsWorker> _logger;

    public ReportsWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<ReportsWorker> logger)
    {
        _scopes = scopes;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using IConnection connection = await RabbitPublisher.Factory(_configuration).CreateConnectionAsync(stoppingToken);
                await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                await channel.QueueDeclareAsync(RabbitPublisher.Queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
                while (!stoppingToken.IsCancellationRequested)
                {
                    BasicGetResult? delivery = await channel.BasicGetAsync(RabbitPublisher.Queue, autoAck: false, stoppingToken);
                    if (delivery is null)
                    {
                        await Task.Delay(300, stoppingToken);
                        continue;
                    }

                    try
                    {
                        ReportMessage message = JsonSerializer.Deserialize<ReportMessage>(delivery.Body.Span, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new JsonException();
                        if (message.ReportId == Guid.Empty)
                        {
                            throw new JsonException();
                        }

                        using IServiceScope scope = _scopes.CreateScope();
                        _logger.LogInformation("Início reportId={ReportId}", message.ReportId);
                        string outcome = await scope.ServiceProvider.GetRequiredService<ReportProcessor>().ProcessAsync(message.ReportId, stoppingToken);
                        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                        _logger.LogInformation("Consumo confirmado reportId={ReportId} outcome={Outcome}", message.ReportId, outcome);
                    }
                    catch (JsonException)
                    {
                        await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, stoppingToken);
                        _logger.LogWarning("Mensagem inválida descartada; nenhum dado da mensagem foi registrado.");
                    }
                    catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogWarning("Persistência indisponível, sem confirmação de consumo; tipo={FailureType}", exception.GetType().Name);
                        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                        await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, stoppingToken);
                    }
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("Conexão indisponível, nova tentativa em 5s; tipo={FailureType}", exception.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
