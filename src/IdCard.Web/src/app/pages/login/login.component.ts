import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { ApiService } from '../../core/api.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="login-wrap">
      <form class="card" (ngSubmit)="submit()">
        <h1>Card Management</h1>
        <p class="muted">Sign in to design and print ID cards</p>
        <label>Username</label>
        <input [(ngModel)]="userName" name="userName" required />
        <label>Password</label>
        <input [(ngModel)]="password" name="password" type="password" required />
        <p class="error" *ngIf="error()">{{ error() }}</p>
        <button type="submit" [disabled]="loading()">Sign in</button>
        <p class="hint" *ngIf="ssoEnabled()">Microsoft SSO is enabled (configure MSAL for full flow).</p>
        <p class="hint">Default: admin / Admin&#64;123</p>
      </form>
    </div>
  `,
  styles: [`
    .login-wrap { min-height: 100vh; display: grid; place-items: center; background: linear-gradient(145deg, #0f2744, #1c4b73 50%, #0b1c2e); }
    .card { width: min(420px, 92vw); background: #fff; border-radius: 16px; padding: 2rem; display: grid; gap: .6rem; box-shadow: 0 20px 50px rgba(0,0,0,.25); }
    h1 { margin: 0; font-family: Georgia, serif; color: #0f2744; }
    .muted, .hint { color: #667; margin: 0 0 .5rem; font-size: .9rem; }
    label { font-size: .85rem; color: #345; }
    input { padding: .7rem .8rem; border: 1px solid #c9d4e0; border-radius: 8px; }
    button { margin-top: .5rem; padding: .8rem; border: 0; border-radius: 8px; background: #1c4b73; color: #fff; font-weight: 600; cursor: pointer; }
    button:disabled { opacity: .6; }
    .error { color: #b00020; margin: 0; }
  `]
})
export class LoginComponent {
  userName = 'admin';
  password = 'Admin@123';
  error = signal('');
  loading = signal(false);
  ssoEnabled = signal(false);

  constructor(private auth: AuthService, private api: ApiService, private router: Router) {
    this.api.ssoStatus().subscribe({ next: s => this.ssoEnabled.set(!!s.enabled), error: () => {} });
  }

  submit() {
    this.loading.set(true);
    this.error.set('');
    this.auth.login(this.userName, this.password).subscribe({
      next: () => { this.loading.set(false); this.router.navigateByUrl('/'); },
      error: (err) => { this.loading.set(false); this.error.set(err?.error?.message || 'Login failed'); }
    });
  }
}
