# Card Management

Centralized ID card platform (CardPresso-style): .NET Core API + Angular web app + thin Windows Print Agent.

## Solution layout

- `src/IdCard.Api` — ASP.NET Core API (auth, projects, print, audit)
- `src/IdCard.Domain` — entities and models
- `src/IdCard.Infrastructure` — SQLite EF Core, encryption, JWT, settings
- `src/IdCard.Connectors` — SQL Server / MySQL / SQLite data connectors
- `src/IdCard.PrintAgent` — Windows print agent (Evolis mock driver for now)
- `src/IdCard.Web` — Angular SPA

## Quick start

### API

```bash
cd src/IdCard.Api
dotnet run --launch-profile http
```

API: `http://localhost:5080`  
Swagger: `http://localhost:5080/swagger`  
Default login: `admin` / `Admin@123`

### Web

```bash
cd src/IdCard.Web
npm install
npm start
```

App: `http://localhost:4200`

### Print Agent

1. In the web app (Admin): **Printers → Generate pairing code**
2. Set the code in `src/IdCard.PrintAgent/appsettings.json` → `Agent:PairingCode`
3. Run:

```bash
cd src/IdCard.PrintAgent
dotnet run
```

## Features (Phase 1)

- Single-org users + roles (Admin / Designer / Operator)
- Optional Microsoft Entra ID SSO settings (secrets encrypted at rest)
- Projects with internal SQLite data or external DB connection
- Visual card designer (JSON front/back)
- Print queue + SignalR agent + mock Evolis driver
- Audit log with filter and CSV export
