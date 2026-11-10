import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { tap } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface LoginResponse {
  token: string;
  userId: string;
  userName: string;
  displayName: string;
  role: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly key = 'cardmgmt_token';
  readonly user = signal<LoginResponse | null>(this.readUser());

  constructor(private http: HttpClient, private router: Router) {}

  login(userName: string, password: string) {
    return this.http.post<LoginResponse>(`${environment.apiUrl}/api/auth/login`, { userName, password }).pipe(
      tap(res => {
        localStorage.setItem(this.key, JSON.stringify(res));
        this.user.set(res);
      })
    );
  }

  logout() {
    localStorage.removeItem(this.key);
    this.user.set(null);
    this.router.navigateByUrl('/login');
  }

  token(): string | null {
    return this.user()?.token ?? null;
  }

  isLoggedIn(): boolean {
    return !!this.token();
  }

  private readUser(): LoginResponse | null {
    const raw = localStorage.getItem(this.key);
    if (!raw) return null;
    try { return JSON.parse(raw); } catch { return null; }
  }
}
