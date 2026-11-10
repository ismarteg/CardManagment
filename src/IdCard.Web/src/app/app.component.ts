import { Component, computed } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { CommonModule } from '@angular/common';
import { AuthService } from './core/auth.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <div class="shell" [class.authed]="loggedIn()">
      <aside *ngIf="loggedIn()">
        <div class="brand">Card Management</div>
        <nav>
          <a routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{exact:true}">Projects</a>
          <a routerLink="/printers" routerLinkActive="active">Printers</a>
          <a routerLink="/audit" routerLinkActive="active">Audit</a>
          <a routerLink="/settings" routerLinkActive="active">Settings</a>
        </nav>
        <div class="user">
          <span>{{ auth.user()?.displayName }}</span>
          <button type="button" (click)="auth.logout()">Logout</button>
        </div>
      </aside>
      <main>
        <router-outlet />
      </main>
    </div>
  `,
  styles: [`
    .shell { min-height: 100vh; display: grid; grid-template-columns: 1fr; background: #eef2f6; }
    .shell.authed { grid-template-columns: 230px 1fr; }
    aside { background: #0f2744; color: #fff; padding: 1.25rem 1rem; display: grid; grid-template-rows: auto 1fr auto; gap: 1.5rem; }
    .brand { font-family: Georgia, 'Times New Roman', serif; font-size: 1.25rem; letter-spacing: .02em; }
    nav { display: grid; gap: .35rem; align-content: start; }
    nav a { color: #c9d7e8; text-decoration: none; padding: .55rem .7rem; border-radius: 8px; }
    nav a.active, nav a:hover { background: rgba(255,255,255,.1); color: #fff; }
    .user { display: grid; gap: .5rem; font-size: .9rem; }
    .user button { border: 0; border-radius: 8px; padding: .45rem; background: #1c4b73; color: #fff; cursor: pointer; }
    main { padding: 1.25rem; }
    @media (max-width: 800px) {
      .shell.authed { grid-template-columns: 1fr; }
      aside { grid-template-rows: auto auto auto; }
      nav { grid-auto-flow: column; overflow: auto; }
    }
  `]
})
export class AppComponent {
  loggedIn = computed(() => !!this.auth.user());
  constructor(public auth: AuthService) {}
}
