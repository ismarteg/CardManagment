import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page">
      <h2>Settings</h2>
      <form class="panel" (ngSubmit)="save()">
        <h3>Microsoft Entra ID SSO</h3>
        <label class="check"><input type="checkbox" [(ngModel)]="enabled" name="enabled" /> Enable Microsoft SSO</label>
        <label>Tenant ID</label>
        <input [(ngModel)]="tenantId" name="tenantId" />
        <label>Client ID</label>
        <input [(ngModel)]="clientId" name="clientId" />
        <label>Client secret {{ hasSecret() ? '(saved — leave blank to keep)' : '' }}</label>
        <input [(ngModel)]="clientSecret" name="clientSecret" type="password" autocomplete="new-password" />
        <label>Redirect URI</label>
        <input [(ngModel)]="redirectUri" name="redirectUri" />
        <button type="submit">Save</button>
        <p *ngIf="msg()">{{ msg() }}</p>
      </form>
    </div>
  `,
  styles: [`
    .page { display: grid; gap: 1rem; max-width: 640px; }
    .panel { background: #fff; border-radius: 12px; padding: 1.25rem; display: grid; gap: .55rem; }
    label { font-size: .85rem; color: #345; }
    input { padding: .6rem .7rem; border: 1px solid #c9d4e0; border-radius: 8px; }
    button { margin-top: .5rem; padding: .7rem; border: 0; border-radius: 8px; background: #1c4b73; color: #fff; cursor: pointer; }
    .check { display: flex; align-items: center; gap: .5rem; }
  `]
})
export class SettingsComponent implements OnInit {
  enabled = false;
  tenantId = '';
  clientId = '';
  clientSecret = '';
  redirectUri = '';
  hasSecret = signal(false);
  msg = signal('');

  constructor(private api: ApiService) {}

  ngOnInit() {
    this.api.getSsoSettings().subscribe(s => {
      this.enabled = !!s.enabled;
      this.tenantId = s.tenantId || '';
      this.clientId = s.clientId || '';
      this.redirectUri = s.redirectUri || '';
      this.hasSecret.set(!!s.hasClientSecret);
    });
  }

  save() {
    this.api.saveSsoSettings({
      enabled: this.enabled,
      tenantId: this.tenantId,
      clientId: this.clientId,
      clientSecret: this.clientSecret || null,
      redirectUri: this.redirectUri
    }).subscribe({
      next: () => { this.msg.set('Saved'); this.clientSecret = ''; this.hasSecret.set(true); },
      error: e => this.msg.set(e?.error?.message || 'Save failed')
    });
  }
}
