# Print Agent

Thin Windows service that connects to the central API and prints to local Evolis (or mock) printers.

## Pairing

1. Sign in as Admin in the web app.
2. Open **Printers** and click **Generate pairing code**.
3. Put the code in `src/IdCard.PrintAgent/appsettings.json`:

```json
{
  "Agent": {
    "ServerUrl": "http://localhost:5080",
    "PairingCode": "YOURCODE",
    "Name": "Badge Desk PC"
  }
}
```

4. Run:

```bash
cd src/IdCard.PrintAgent
dotnet run
```

On success, the agent saves `agent-config.local.json` (gitignored) with `AgentId` and token.

## Drivers

- Current: `MockEvolisPrinterDriver` (dev/demo)
- Next: real Evolis SDK behind `ICardPrinterDriver`
