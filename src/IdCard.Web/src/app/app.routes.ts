import { Routes } from '@angular/router';
import { LoginComponent } from './pages/login/login.component';
import { ProjectsComponent } from './pages/projects/projects.component';
import { ProjectDetailComponent } from './pages/project-detail/project-detail.component';
import { PrintersComponent } from './pages/printers/printers.component';
import { AuditComponent } from './pages/audit/audit.component';
import { SettingsComponent } from './pages/settings/settings.component';
import { authGuard } from './core/auth.guard';

export const routes: Routes = [
  { path: 'login', component: LoginComponent },
  { path: '', component: ProjectsComponent, canActivate: [authGuard] },
  { path: 'projects/:id', component: ProjectDetailComponent, canActivate: [authGuard] },
  { path: 'printers', component: PrintersComponent, canActivate: [authGuard] },
  { path: 'audit', component: AuditComponent, canActivate: [authGuard] },
  { path: 'settings', component: SettingsComponent, canActivate: [authGuard] },
  { path: '**', redirectTo: '' }
];
