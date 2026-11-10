import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';

@Component({
  selector: 'app-printers',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page">
      <header>
        <h2>Printers & Agents</h2>
        <button (click)="createCode()">Generate pairing code</button>
      </header>

      <p class="code" *ngIf="pairingCode()">Pairing code: <strong>{{ pairingCode() }}</strong> (expires {{ expiresAt() | date:'short' }})</p>

      <section class="panel">
        <h3>Printers</h3>
        <table>
          <thead><tr><th>Name</th><th>Model</th><th>Agent</th><th>Status</th></tr></thead>
          <tbody>
            <tr *ngFor="let p of printers()">
              <td>{{ p.name }}</td>
              <td>{{ p.model }}</td>
              <td>{{ p.agentName || '-' }}</td>
              <td>{{ p.isOnline || p.agentOnline ? 'Online' : 'Offline' }}</td>
            </tr>
          </tbody>
        </table>
      </section>

      <section class="panel">
        <h3>Agents</h3>
        <table>
          <thead><tr><th>Name</th><th>Machine</th><th>Online</th><th>Last seen</th></tr></thead>
          <tbody>
            <tr *ngFor="let a of agents()">
              <td>{{ a.name }}</td>
              <td>{{ a.machineName }}</td>
              <td>{{ a.isOnline ? 'Yes' : 'No' }}</td>
              <td>{{ a.lastHeartbeatAtUtc | date:'short' }}</td>
            </tr>
          </tbody>
        </table>
      </section>

      <section class="panel">
        <h3>Register printer manually</h3>
        <form class="row" (ngSubmit)="addPrinter()">
          <input [(ngModel)]="name" name="name" placeholder="Printer name" required />
          <input [(ngModel)]="model" name="model" placeholder="Model (Evolis)" />
          <button type="submit">Add</button>
        </form>
      </section>
    </div>
  `,
  styles: [`
    .page { display: grid; gap: 1rem; }
    header { display: flex; justify-content: space-between; align-items: center; }
    button { padding: .55rem .9rem; border: 0; border-radius: 8px; background: #1c4b73; color: #fff; cursor: pointer; }
    .panel { background: #fff; border-radius: 12px; padding: 1rem; }
    table { width: 100%; border-collapse: collapse; }
    th, td { text-align: left; padding: .5rem; border-bottom: 1px solid #e6edf5; }
    .code { background: #eef5fb; padding: .8rem 1rem; border-radius: 8px; }
    .row { display: flex; gap: .5rem; flex-wrap: wrap; }
    input { padding: .55rem .7rem; border: 1px solid #c9d4e0; border-radius: 8px; }
  `]
})
export class PrintersComponent implements OnInit {
  printers = signal<any[]>([]);
  agents = signal<any[]>([]);
  pairingCode = signal('');
  expiresAt = signal<string | null>(null);
  name = '';
  model = 'Evolis';

  constructor(private api: ApiService) {}

  ngOnInit() { this.reload(); }

  reload() {
    this.api.getPrinters().subscribe(p => this.printers.set(p));
    this.api.getAgents().subscribe({ next: a => this.agents.set(a), error: () => this.agents.set([]) });
  }

  createCode() {
    this.api.createPairingCode().subscribe(r => {
      this.pairingCode.set(r.code);
      this.expiresAt.set(r.expiresAtUtc);
    });
  }

  addPrinter() {
    this.api.createPrinter({ name: this.name, model: this.model }).subscribe(() => {
      this.name = '';
      this.reload();
    });
  }
}
