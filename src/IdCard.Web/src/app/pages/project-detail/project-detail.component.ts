import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { DesignerComponent } from '../designer/designer.component';

@Component({
  selector: 'app-project-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, DesignerComponent],
  template: `
    <div class="page" *ngIf="project() as p">
      <header>
        <div>
          <a routerLink="/">← Projects</a>
          <h2>{{ p.name }}</h2>
        </div>
        <div class="tabs">
          <button [class.active]="tab()==='data'" (click)="tab.set('data')">Data</button>
          <button [class.active]="tab()==='design'" (click)="tab.set('design')">Design</button>
          <button [class.active]="tab()==='print'" (click)="tab.set('print')">Print</button>
          <button [class.active]="tab()==='connection'" (click)="tab.set('connection')" *ngIf="p.dataMode===1">Connection</button>
        </div>
      </header>

      <section *ngIf="tab()==='data'" class="panel">
        <div class="row">
          <input [(ngModel)]="search" placeholder="Search..." (keyup.enter)="loadRecords()" />
          <button (click)="loadRecords()">Search</button>
          <button (click)="showNew.set(true)" *ngIf="p.dataMode===0">Add record</button>
          <label class="file" *ngIf="p.dataMode===0">
            Import CSV
            <input type="file" accept=".csv" (change)="onCsv($event)" hidden />
          </label>
        </div>

        <form class="new" *ngIf="showNew()" (ngSubmit)="createRecord()">
          <input [(ngModel)]="newKey" name="key" placeholder="Key / ID" />
          <input [(ngModel)]="newFullName" name="fullName" placeholder="Full name" />
          <input [(ngModel)]="newStudentId" name="studentId" placeholder="Student ID" />
          <button type="submit">Save</button>
        </form>

        <table>
          <thead>
            <tr>
              <th>Key</th>
              <th>Data</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let r of records()" [class.sel]="selected()?.key===r.key" (click)="selected.set(r)">
              <td>{{ r.key }}</td>
              <td><code>{{ r.data | json }}</code></td>
              <td>
                <button *ngIf="r.id" class="link" (click)="removeRecord(r); $event.stopPropagation()">Delete</button>
              </td>
            </tr>
          </tbody>
        </table>
      </section>

      <section *ngIf="tab()==='design'" class="panel">
        <app-designer [designJson]="p.designJson" [saveHandler]="saveDesign"></app-designer>
      </section>

      <section *ngIf="tab()==='print'" class="panel">
        <div class="row">
          <select [(ngModel)]="printerId" name="printerId">
            <option value="">Select printer</option>
            <option *ngFor="let pr of printers()" [value]="pr.id">{{ pr.name }} {{ pr.isOnline ? '(online)' : '(offline)' }}</option>
          </select>
          <button (click)="preview()" [disabled]="!selected()">Preview</button>
          <button class="primary" (click)="print()" [disabled]="!selected() || !printerId">Print selected</button>
        </div>
        <div class="preview" *ngIf="previewUrls() as urls">
          <img *ngIf="urls.frontImage" [src]="apiBase + urls.frontImage" alt="front" />
          <img *ngIf="urls.backImage" [src]="apiBase + urls.backImage" alt="back" />
        </div>
        <h3>Recent jobs</h3>
        <ul>
          <li *ngFor="let j of jobs()">{{ j.recordKey }} — {{ j.status }} <small>{{ j.createdAtUtc | date:'short' }}</small></li>
        </ul>
      </section>

      <section *ngIf="tab()==='connection'" class="panel">
        <label>Database type</label>
        <select [(ngModel)]="extDbType" name="extDbType">
          <option [ngValue]="1">SQL Server</option>
          <option [ngValue]="2">MySQL</option>
          <option [ngValue]="0">SQLite file</option>
        </select>
        <label>Connection string</label>
        <textarea [(ngModel)]="connectionString" name="cs" rows="3"></textarea>
        <label>Table / view</label>
        <input [(ngModel)]="tableName" name="table" />
        <div class="row">
          <button type="button" (click)="testConn()">Test</button>
          <button type="button" (click)="loadTables()">Discover tables</button>
          <button type="button" class="primary" (click)="saveConnection()">Save connection</button>
        </div>
        <p *ngIf="connMsg()">{{ connMsg() }}</p>
        <ul>
          <li *ngFor="let t of tables()" (click)="tableName=t" style="cursor:pointer">{{ t }}</li>
        </ul>
      </section>
    </div>
  `,
  styles: [`
    .page { display: grid; gap: 1rem; }
    header { display: flex; justify-content: space-between; gap: 1rem; flex-wrap: wrap; align-items: end; }
    a { color: #1c4b73; text-decoration: none; }
    .tabs button, .row button, .file, .new button, .link { padding: .5rem .8rem; border-radius: 8px; border: 1px solid #c9d4e0; background: #fff; cursor: pointer; }
    .tabs .active, .primary { background: #1c4b73; color: #fff; border-color: #1c4b73; }
    .panel { background: #fff; border-radius: 12px; padding: 1rem; display: grid; gap: .75rem; }
    .row { display: flex; flex-wrap: wrap; gap: .5rem; align-items: center; }
    table { width: 100%; border-collapse: collapse; font-size: .92rem; }
    th, td { border-bottom: 1px solid #e6edf5; padding: .55rem; text-align: left; vertical-align: top; }
    tr.sel { background: #eef5fb; }
    code { white-space: pre-wrap; font-size: .8rem; }
    .preview { display: flex; gap: 1rem; flex-wrap: wrap; }
    .preview img { max-width: 320px; border: 1px solid #ccd; background: #fff; }
    textarea, input, select { padding: .55rem .7rem; border: 1px solid #c9d4e0; border-radius: 8px; }
    .new { display: flex; flex-wrap: wrap; gap: .5rem; }
    .link { background: transparent; border: 0; color: #b00020; }
  `]
})
export class ProjectDetailComponent implements OnInit {
  project = signal<any>(null);
  records = signal<any[]>([]);
  printers = signal<any[]>([]);
  jobs = signal<any[]>([]);
  selected = signal<any>(null);
  previewUrls = signal<any>(null);
  tab = signal<'data' | 'design' | 'print' | 'connection'>('data');
  showNew = signal(false);
  tables = signal<string[]>([]);
  connMsg = signal('');
  search = '';
  printerId = '';
  newKey = '';
  newFullName = '';
  newStudentId = '';
  connectionString = '';
  tableName = '';
  extDbType = 1;
  apiBase = 'http://localhost:5080';
  private id = '';

  saveDesign = (json: string) => {
    const p = this.project();
    if (!p) return;
    this.api.updateProject(this.id, {
      ...this.toUpdateBody(p),
      designJson: json
    }).subscribe(updated => this.project.set(updated));
  };

  constructor(private route: ActivatedRoute, private api: ApiService) {}

  ngOnInit() {
    this.id = this.route.snapshot.paramMap.get('id') || '';
    this.api.getProject(this.id).subscribe(p => {
      this.project.set(p);
      this.extDbType = p.externalDatabaseType ?? 1;
      this.tableName = p.externalTableName || '';
      this.loadRecords();
    });
    this.api.getPrinters().subscribe(list => this.printers.set(list));
    this.api.getJobs(this.id).subscribe(list => this.jobs.set(list));
  }

  loadRecords() {
    this.api.getRecords(this.id, this.search).subscribe(list => this.records.set(list));
  }

  createRecord() {
    this.api.createRecord(this.id, {
      key: this.newKey || this.newStudentId,
      data: { FullName: this.newFullName, StudentId: this.newStudentId }
    }).subscribe(() => {
      this.showNew.set(false);
      this.newKey = this.newFullName = this.newStudentId = '';
      this.loadRecords();
    });
  }

  removeRecord(r: any) {
    if (!r.id || !confirm('Delete record?')) return;
    this.api.deleteRecord(this.id, r.id).subscribe(() => this.loadRecords());
  }

  onCsv(ev: Event) {
    const file = (ev.target as HTMLInputElement).files?.[0];
    if (!file) return;
    this.api.importCsv(this.id, file).subscribe(() => this.loadRecords());
  }

  preview() {
    const sel = this.selected();
    this.api.preview(this.id, sel?.id).subscribe(urls => {
      this.previewUrls.set({
        frontImage: this.fixPath(urls.frontImage),
        backImage: this.fixPath(urls.backImage)
      });
    });
  }

  print() {
    const sel = this.selected();
    if (!sel || !this.printerId) return;
    this.api.queuePrint({
      projectId: this.id,
      printerId: this.printerId,
      records: [{ key: sel.key, data: sel.data || {} }]
    }).subscribe(() => {
      this.api.getJobs(this.id).subscribe(list => this.jobs.set(list));
      alert('Print job queued');
    });
  }

  testConn() {
    this.api.testConnection({ databaseType: this.extDbType, connectionString: this.connectionString })
      .subscribe({
        next: r => this.connMsg.set(r.success ? 'Connection OK' : 'Failed'),
        error: e => this.connMsg.set(e?.error?.message || 'Failed')
      });
  }

  loadTables() {
    this.api.discoverTables({ databaseType: this.extDbType, connectionString: this.connectionString })
      .subscribe(t => this.tables.set(t));
  }

  saveConnection() {
    const p = this.project();
    if (!p) return;
    this.api.updateProject(this.id, {
      ...this.toUpdateBody(p),
      dataMode: 1,
      externalDatabaseType: this.extDbType,
      connectionString: this.connectionString,
      externalTableName: this.tableName
    }).subscribe(updated => {
      this.project.set(updated);
      this.connMsg.set('Saved');
    });
  }

  private toUpdateBody(p: any) {
    return {
      name: p.name,
      description: p.description,
      cardSize: p.cardSize,
      dualSided: p.dualSided,
      dataMode: p.dataMode,
      externalDatabaseType: p.externalDatabaseType,
      externalTableName: p.externalTableName,
      allowExternalWriteBack: p.allowExternalWriteBack,
      fieldMappings: p.fieldMappings,
      defaultPrinterId: p.defaultPrinterId
    };
  }

  private fixPath(path?: string) {
    if (!path) return path;
    // API returns /files/uploads/... or absolute; normalize for static mapping
    if (path.startsWith('/files/')) return path.replace('/files/', '/files/uploads/').replace('/files/uploads/uploads/', '/files/uploads/');
    return path;
  }
}
