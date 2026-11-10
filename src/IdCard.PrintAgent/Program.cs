using IdCard.PrintAgent;
using IdCard.PrintAgent.Printers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
builder.Services.AddHttpClient("api", (sp, client) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentOptions>>().Value;
    client.BaseAddress = new Uri(options.ServerUrl.TrimEnd('/') + "/");
});
builder.Services.AddSingleton<ICardPrinterDriver, MockEvolisPrinterDriver>();
builder.Services.AddHostedService<AgentWorker>();

var host = builder.Build();
host.Run();
