namespace IdCard.PrintAgent.Printers;

public interface ICardPrinterDriver
{
    string Name { get; }
    Task<IReadOnlyList<LocalPrinterInfo>> DiscoverAsync(CancellationToken ct = default);
    Task PrintAsync(string systemPrinterName, string frontImagePath, string? backImagePath, CancellationToken ct = default);
}

public record LocalPrinterInfo(string Name, string SystemPrinterName, string Model, string? StatusMessage);
