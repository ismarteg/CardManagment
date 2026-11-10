using System.Net.Http.Json;
using IdCard.PrintAgent.Printers;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace IdCard.PrintAgent;

public class AgentWorker : BackgroundService
{
    private readonly ILogger<AgentWorker> _logger;
    private readonly ICardPrinterDriver _driver;
    private readonly AgentOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private HubConnection? _hub;

    public AgentWorker(
        ILogger<AgentWorker> logger,
        ICardPrinterDriver driver,
        IOptions<AgentOptions> options,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _driver = driver;
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsurePairedAsync(stoppingToken);

        var hubUrl = $"{_options.ServerUrl.TrimEnd('/')}/hubs/print-agent?agentId={_options.AgentId}";
        _hub = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _hub.On<object>("PrintJob", async payload =>
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(payload);
                var job = System.Text.Json.JsonSerializer.Deserialize<PrintJobMessage>(json, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (job is null) return;

                await _hub.InvokeAsync("ReportJobStatus", job.JobId, "Printing", null, stoppingToken);
                await _driver.PrintAsync(job.SystemPrinterName ?? string.Empty, job.FrontImagePath!, job.BackImagePath, stoppingToken);
                await _hub.InvokeAsync("ReportJobStatus", job.JobId, "Done", null, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Print job failed");
                try
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(payload);
                    var job = System.Text.Json.JsonSerializer.Deserialize<PrintJobMessage>(json, new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (job is not null)
                        await _hub.InvokeAsync("ReportJobStatus", job.JobId, "Failed", ex.Message, stoppingToken);
                }
                catch { /* ignore */ }
            }
        });

        await _hub.StartAsync(stoppingToken);
        _logger.LogInformation("Print agent connected to {Url}", hubUrl);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_hub.State == HubConnectionState.Connected && _options.AgentId is not null)
                    await _hub.InvokeAsync("Heartbeat", _options.AgentId.Value, "ok", stoppingToken);

                await SyncPrintersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Heartbeat/sync failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    private async Task EnsurePairedAsync(CancellationToken ct)
    {
        if (_options.AgentId is not null && !string.IsNullOrWhiteSpace(_options.Token))
            return;

        if (string.IsNullOrWhiteSpace(_options.PairingCode))
            throw new InvalidOperationException("Set Agent:PairingCode (or AgentId+Token) in appsettings.");

        var printers = await _driver.DiscoverAsync(ct);
        var client = _httpClientFactory.CreateClient("api");
        var response = await client.PostAsJsonAsync("api/agents/pair", new
        {
            code = _options.PairingCode,
            name = _options.Name ?? Environment.MachineName,
            machineName = Environment.MachineName,
            printers = printers.Select(p => new
            {
                name = p.Name,
                model = p.Model,
                systemPrinterName = p.SystemPrinterName,
                statusMessage = p.StatusMessage
            })
        }, ct);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PairResult>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Pairing failed");

        _options.AgentId = result.AgentId;
        _options.Token = result.Token;
        await SaveLocalConfigAsync();
        _logger.LogInformation("Paired as agent {AgentId}", result.AgentId);
    }

    private async Task SyncPrintersAsync(CancellationToken ct)
    {
        if (_options.AgentId is null || string.IsNullOrWhiteSpace(_options.Token)) return;
        var printers = await _driver.DiscoverAsync(ct);
        var client = _httpClientFactory.CreateClient("api");
        await client.PostAsJsonAsync($"api/agents/{_options.AgentId}/printers/sync", new
        {
            token = _options.Token,
            printers = printers.Select(p => new
            {
                name = p.Name,
                model = p.Model,
                systemPrinterName = p.SystemPrinterName,
                statusMessage = p.StatusMessage
            })
        }, ct);
    }

    private async Task SaveLocalConfigAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "agent-config.local.json");
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            Agent = new
            {
                _options.ServerUrl,
                _options.AgentId,
                _options.Token,
                _options.Name
            }
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_hub is not null)
            await _hub.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}

public class AgentOptions
{
    public string ServerUrl { get; set; } = "http://localhost:5080";
    public string? PairingCode { get; set; }
    public Guid? AgentId { get; set; }
    public string? Token { get; set; }
    public string? Name { get; set; }
}

public record PairResult(Guid AgentId, string Token, string Name);
public record PrintJobMessage(Guid JobId, Guid PrinterId, string? SystemPrinterName, string? FrontImagePath, string? BackImagePath, string? RecordKey);
