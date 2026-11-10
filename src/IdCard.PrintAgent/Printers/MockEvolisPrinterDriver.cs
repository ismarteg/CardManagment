using Microsoft.Extensions.Logging;

namespace IdCard.PrintAgent.Printers;

/// <summary>
/// Mock Evolis driver for development. Replace with real Evolis SDK integration.
/// </summary>
public class MockEvolisPrinterDriver : ICardPrinterDriver
{
    private readonly ILogger<MockEvolisPrinterDriver> _logger;

    public MockEvolisPrinterDriver(ILogger<MockEvolisPrinterDriver> logger)
    {
        _logger = logger;
    }

    public string Name => "Evolis (Mock)";

    public Task<IReadOnlyList<LocalPrinterInfo>> DiscoverAsync(CancellationToken ct = default)
    {
        IReadOnlyList<LocalPrinterInfo> list =
        [
            new("Evolis Primacy (Mock)", "EVOLIS_MOCK_PRIMACY", "Evolis Primacy", "Ready"),
            new("Evolis Zenius (Mock)", "EVOLIS_MOCK_ZENIUS", "Evolis Zenius", "Ready")
        ];
        return Task.FromResult(list);
    }

    public async Task PrintAsync(string systemPrinterName, string frontImagePath, string? backImagePath, CancellationToken ct = default)
    {
        _logger.LogInformation("Mock print to {Printer}: front={Front} back={Back}", systemPrinterName, frontImagePath, backImagePath);
        await Task.Delay(800, ct);
        if (!File.Exists(frontImagePath))
            throw new FileNotFoundException("Front image missing", frontImagePath);
    }
}
