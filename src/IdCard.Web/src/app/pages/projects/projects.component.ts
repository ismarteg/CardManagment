import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';

interface ColumnDraft {
  name: string;
  label: string;
  dataType: string;
  isKey: boolean;
  required: boolean;
}

@Component({
  selector: 'app-projects',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="page">
      <header>
        <h2>Projects</h2>
        <button (click)="toggleForm()">{{ showForm() ? 'Cancel' : 'New project' }}</button>
      </header>

      <form class="panel" *ngIf="showForm()" (ngSubmit)="create()">
        <div class="row">
          <input [(ngModel)]="name" name="name" placeholder="Project name" required />
          <select [(ngModel)]="dataMode" name="dataMode">
            <option [ngValue]="0">Internal SQLite</option>
            <option [ngValue]="1">External database</option>
          </select>
        </div>

        <div class="schema" *ngIf="dataMode === 0">
          <h3>Create internal table</h3>
          <p class="hint">Define the table and columns you need for card data (e.g. Students).</p>
          <input [(ngModel)]="tableName" name="tableName" placeholder="Table name (e.g. Students)" required />

          <table class="cols">
            <thead>
              <tr>
                <th>Column</th>
                <th>Label</th>
                <th>Type</th>
                <th>Key</th>
                <th>Required</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let col of columns; let i = index">
                <td><input [(ngModel)]="col.name" [name]="'c'+i" placeholder="StudentId" required /></td>
                <td><input [(ngModel)]="col.label" [name]="'l'+i" placeholder="Student ID" /></td>
                <td>
                  <select [(ngModel)]="col.dataType" [name]="'t'+i">
                    <option value="string">Text</option>
                    <option value="number">Number</option>
                    <option value="date">Date</option>
                    <option value="photo">Photo (file folder)</option>
                    <option value="photo_binary">Photo (binary in DB)</option>
                  </select>
                </td>
                <td><input type="checkbox" [(ngModel)]="col.isKey" [name]="'k'+i" (change)="onKeyChange(i)" /></td>
                <td><input type="checkbox" [(ngModel)]="col.required" [name]="'r'+i" /></td>
                <td><button type="button" class="link" (click)="removeColumn(i)" [disabled]="columns.length<=1">Remove</button></td>
              </tr>
            </tbody>
          </table>
          <button type="button" class="secondary" (click)="addColumn()">Add column</button>
        </div>

        <button type="submit">Create project</button>
        <p class="error" *ngIf="error()">{{ error() }}</p>
      </form>

      <div class="grid">
        <a class="item" *ngFor="let p of projects()" [routerLink]="['/projects', p.id]">
          <strong>{{ p.name }}</strong>
          <span>
            {{ p.dataMode === 0 ? ('Internal · ' + (p.internalTableName || 'Table')) : 'External' }}
            · {{ (p.fieldMappings || []).length }} fields
          </span>
        </a>
      </div>
    </div>
  `,
  styles: [`
    .page { display: grid; gap: 1rem; }
    header { display: flex; justify-content: space-between; align-items: center; }
    .panel { display: grid; gap: .85rem; background: #fff; padding: 1rem; border-radius: 12px; }
    .row { display: flex; gap: .5rem; flex-wrap: wrap; }
    .schema { display: grid; gap: .6rem; border-top: 1px solid #e6edf5; padding-top: .8rem; }
    .hint { margin: 0; color: #678; font-size: .9rem; }
    .cols { width: 100%; border-collapse: collapse; }
    .cols th, .cols td { text-align: left; padding: .35rem; border-bottom: 1px solid #eef2f6; }
    input, select, button { padding: .55rem .7rem; border-radius: 8px; border: 1px solid #c9d4e0; }
    button { background: #1c4b73; color: #fff; border: 0; cursor: pointer; width: fit-content; }
    .secondary { background: #e8eef5; color: #1c4b73; }
    .link { background: transparent; border: 0; color: #b00020; padding: .2rem; }
    .error { color: #b00020; margin: 0; }
    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 1rem; }
    .item { background: #fff; border-radius: 12px; padding: 1rem; text-decoration: none; color: inherit; display: grid; gap: .35rem; box-shadow: 0 8px 24px rgba(15,39,68,.06); }
    .item span { color: #678; font-size: .9rem; }
  `]
})
export class ProjectsComponent implements OnInit {
  projects = signal<any[]>([]);
  showForm = signal(false);
  error = signal('');
  name = '';
  dataMode = 0;
  tableName = 'Students';
  columns: ColumnDraft[] = [
    { name: 'StudentId', label: 'Student ID', dataType: 'string', isKey: true, required: true },
    { name: 'FullName', label: 'Full name', dataType: 'string', isKey: false, required: true },
    { name: 'Photo', label: 'Photo', dataType: 'photo', isKey: false, required: false }
  ];

  constructor(private api: ApiService, private router: Router) {}

  ngOnInit() { this.reload(); }

  reload() {
    this.api.getProjects().subscribe(list => this.projects.set(list));
  }

  toggleForm() {
    this.showForm.set(!this.showForm());
    this.error.set('');
  }

  addColumn() {
    this.columns.push({ name: '', label: '', dataType: 'string', isKey: false, required: false });
  }

  removeColumn(i: number) {
    if (this.columns.length <= 1) return;
    this.columns.splice(i, 1);
  }

  onKeyChange(i: number) {
    if (!this.columns[i].isKey) return;
    this.columns.forEach((c, idx) => {
      if (idx !== i) c.isKey = false;
      else c.required = true;
    });
  }

  create() {
    this.error.set('');
    const fieldMappings = this.dataMode === 0
      ? this.columns
          .filter(c => c.name.trim())
          .map(c => ({
            designField: c.name.trim(),
            sourceColumn: c.name.trim(),
            label: c.label.trim() || c.name.trim(),
            dataType: c.dataType,
            isPhoto: c.dataType === 'photo' || c.dataType === 'photo_binary',
            isKey: c.isKey,
            required: c.required || c.isKey
          }))
      : [];

    if (this.dataMode === 0 && fieldMappings.length === 0) {
      this.error.set('Add at least one column for the internal table.');
      return;
    }

    this.api.createProject({
      name: this.name,
      cardSize: 'CR80',
      dualSided: true,
      dataMode: this.dataMode,
      internalTableName: this.dataMode === 0 ? this.tableName : null,
      allowExternalWriteBack: false,
      fieldMappings
    }).subscribe({
      next: (p) => {
        this.name = '';
        this.showForm.set(false);
        this.reload();
        if (p?.id) this.router.navigate(['/projects', p.id]);
      },
      error: (e) => this.error.set(e?.error?.message || 'Create failed')
    });
  }
}
