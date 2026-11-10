import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';

@Component({
  selector: 'app-projects',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="page">
      <header>
        <h2>Projects</h2>
        <button (click)="showForm.set(!showForm())">{{ showForm() ? 'Cancel' : 'New project' }}</button>
      </header>

      <form class="panel" *ngIf="showForm()" (ngSubmit)="create()">
        <input [(ngModel)]="name" name="name" placeholder="Project name" required />
        <select [(ngModel)]="dataMode" name="dataMode">
          <option [ngValue]="0">Internal SQLite</option>
          <option [ngValue]="1">External database</option>
        </select>
        <button type="submit">Create</button>
      </form>

      <div class="grid">
        <a class="item" *ngFor="let p of projects()" [routerLink]="['/projects', p.id]">
          <strong>{{ p.name }}</strong>
          <span>{{ p.dataMode === 0 ? 'Internal' : 'External' }} · v{{ p.designVersion }}</span>
        </a>
      </div>
    </div>
  `,
  styles: [`
    .page { display: grid; gap: 1rem; }
    header { display: flex; justify-content: space-between; align-items: center; }
    .panel { display: flex; gap: .5rem; flex-wrap: wrap; background: #fff; padding: 1rem; border-radius: 12px; }
    input, select, button { padding: .6rem .8rem; border-radius: 8px; border: 1px solid #c9d4e0; }
    button { background: #1c4b73; color: #fff; border: 0; cursor: pointer; }
    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 1rem; }
    .item { background: #fff; border-radius: 12px; padding: 1rem; text-decoration: none; color: inherit; display: grid; gap: .35rem; box-shadow: 0 8px 24px rgba(15,39,68,.06); }
    .item span { color: #678; font-size: .9rem; }
  `]
})
export class ProjectsComponent implements OnInit {
  projects = signal<any[]>([]);
  showForm = signal(false);
  name = '';
  dataMode = 0;

  constructor(private api: ApiService) {}

  ngOnInit() { this.reload(); }

  reload() {
    this.api.getProjects().subscribe(list => this.projects.set(list));
  }

  create() {
    this.api.createProject({
      name: this.name,
      cardSize: 'CR80',
      dualSided: true,
      dataMode: this.dataMode,
      allowExternalWriteBack: false,
      fieldMappings: [
        { designField: 'FullName', sourceColumn: 'FullName', dataType: 'string' },
        { designField: 'StudentId', sourceColumn: 'StudentId', dataType: 'string' },
        { designField: 'Photo', sourceColumn: 'Photo', dataType: 'string', isPhoto: true }
      ]
    }).subscribe(() => {
      this.name = '';
      this.showForm.set(false);
      this.reload();
    });
  }
}
