import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-audit',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page">
      <header>
        <h2>Audit log</h2>
        <a [href]="exportUrl" target="_blank">Export CSV</a>
      </header>

      <form class="filters" (ngSubmit)="load()">
        <input [(ngModel)]="action" name="action" placeholder="Action contains..." />
        <input [(ngModel)]="userName" name="userName" placeholder="User" />
        <select [(ngModel)]="severity" name="severity">
          <option value="">All severities</option>
          <option value="0">Info</option>
          <option value="1">Warning</option>
          <option value="2">Error</option>
        </select>
        <button type="submit">Filter</button>
      </form>

      <p>Total: {{ total() }}</p>
      <table>
        <thead>
          <tr><th>When</th><th>Actor</th><th>User</th><th>Action</th><th>Severity</th><th>Details</th></tr>
        </thead>
        <tbody>
          <tr *ngFor="let a of items()">
            <td>{{ a.createdAtUtc | date:'short' }}</td>
            <td>{{ a.actorType }}</td>
            <td>{{ a.userName }}</td>
            <td>{{ a.action }}</td>
            <td>{{ a.severity }}</td>
            <td><code>{{ a.detailsJson }}</code></td>
          </tr>
        </tbody>
      </table>
    </div>
  `,
  styles: [`
    .page { display: grid; gap: 1rem; }
    header { display: flex; justify-content: space-between; align-items: center; }
    a { color: #1c4b73; }
    .filters { display: flex; gap: .5rem; flex-wrap: wrap; background: #fff; padding: 1rem; border-radius: 12px; }
    input, select, button { padding: .55rem .7rem; border-radius: 8px; border: 1px solid #c9d4e0; }
    button { background: #1c4b73; color: #fff; border: 0; cursor: pointer; }
    table { width: 100%; border-collapse: collapse; background: #fff; border-radius: 12px; overflow: hidden; }
    th, td { text-align: left; padding: .55rem; border-bottom: 1px solid #e6edf5; vertical-align: top; font-size: .9rem; }
    code { font-size: .75rem; white-space: pre-wrap; }
  `]
})
export class AuditComponent implements OnInit {
  items = signal<any[]>([]);
  total = signal(0);
  action = '';
  userName = '';
  severity = '';
  exportUrl = `${environment.apiUrl}/api/audit/export`;

  constructor(private api: ApiService) {}

  ngOnInit() { this.load(); }

  load() {
    this.api.getAudit({ action: this.action, userName: this.userName, severity: this.severity }).subscribe(r => {
      this.items.set(r.items || []);
      this.total.set(r.total || 0);
    });
  }
}
