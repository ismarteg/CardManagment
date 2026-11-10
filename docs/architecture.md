# Architecture

## Components

- **Angular SPA** — design, data CRUD, print initiation, settings, audit
- **ASP.NET Core API** — auth, projects, connectors, render, job queue, SignalR hub
- **SQLite app DB** — users, settings (encrypted secrets), projects, printers, agents, jobs, audit
- **Print Agent** — thin Windows service; pairs with server; prints via `ICardPrinterDriver` (mock Evolis now)

## Project model

Each project stores data mode (internal/external), encrypted connection, field mappings, and versioned design JSON (front/back).

## Print flow

1. Operator selects records in web UI
2. API renders PNGs (SkiaSharp) and creates `PrintJob`
3. SignalR pushes job to agent group
4. Agent prints and reports status → audit log
