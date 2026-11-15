import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { DesignerComponent } from '../designer/designer.component';
import { environment } from '../../../environments/environment';

interface FieldDef {
  designField: string;
  sourceColumn: string;
  label?: string;
  dataType?: string;
  isPhoto?: boolean;
  isKey?: boolean;
  required?: boolean;
}

interface DiscoveredColumn {
  name: string;
  rawType: string;
  label: string;
  dataType: string;
  selected: boolean;
  isKey: boolean;
}

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
          <p class="sub" *ngIf="p.dataMode===0">Table: <strong>{{ p.internalTableName || 'Untitled' }}</strong></p>
        </div>
        <div class="tabs">
          <button [class.active]="tab()==='data'" (click)="tab.set('data')">Data</button>
          <button [class.active]="tab()==='schema'" (click)="tab.set('schema')" *ngIf="p.dataMode===0">Table schema</button>
          <button [class.active]="tab()==='design'" (click)="tab.set('design')">Design</button>
          <button [class.active]="tab()==='print'" (click)="tab.set('print')">Print</button>
          <button [class.active]="tab()==='connection'" (click)="tab.set('connection')" *ngIf="p.dataMode===1">Connection</button>
        </div>
      </header>

      <section *ngIf="tab()==='schema'" class="panel">
        <h3>Internal table schema</h3>
        <label>Table name</label>
        <input [(ngModel)]="schemaTableName" name="schemaTableName" />
        <table class="cols">
          <thead>
            <tr><th>Column</th><th>Label</th><th>Type</th><th>Key</th><th>Required</th><th></th></tr>
          </thead>
          <tbody>
            <tr *ngFor="let col of schemaFields; let i = index">
              <td><input [(ngModel)]="col.sourceColumn" [name]="'sc'+i" /></td>
              <td><input [(ngModel)]="col.label" [name]="'sl'+i" /></td>
              <td>
                <select [(ngModel)]="col.dataType" [name]="'st'+i" (ngModelChange)="onTypeChange(col, $event)">
                  <option value="string">Text</option>
                  <option value="number">Number</option>
                  <option value="date">Date</option>
                  <option value="photo">Photo (file folder)</option>
                  <option value="photo_binary">Photo (binary in DB)</option>
                </select>
              </td>
              <td><input type="checkbox" [(ngModel)]="col.isKey" [name]="'sk'+i" (change)="onSchemaKey(i)" /></td>
              <td><input type="checkbox" [(ngModel)]="col.required" [name]="'sr'+i" /></td>
              <td><button type="button" class="link" (click)="schemaFields.splice(i,1)">Remove</button></td>
            </tr>
          </tbody>
        </table>
        <div class="row">
          <button type="button" class="secondary" (click)="addSchemaField()">Add column</button>
          <button type="button" class="primary" (click)="saveSchema()">Save schema</button>
        </div>
        <p *ngIf="schemaMsg()">{{ schemaMsg() }}</p>
      </section>

      <section *ngIf="tab()==='data'" class="data-layout">
        <div class="data-main panel">
          <div class="subtabs">
            <button type="button" [class.active]="dataTab()==='list'" (click)="dataTab.set('list')">List</button>
            <button type="button" *ngIf="p.dataMode===0" [class.active]="dataTab()==='form'" (click)="openNew()" [disabled]="!(p.fieldMappings||[]).length">Form</button>
            <div class="spacer"></div>
            <input [(ngModel)]="search" placeholder="Search..." (keyup.enter)="loadRecords()" />
            <button type="button" (click)="loadRecords()">Search</button>
            <label class="file" *ngIf="p.dataMode===0">
              Import CSV
              <input type="file" accept=".csv" (change)="onCsv($event)" hidden />
            </label>
          </div>

          <p class="hint" *ngIf="p.dataMode===0 && !(p.fieldMappings||[]).length">
            No columns yet. Open <button type="button" class="link" (click)="tab.set('schema')">Table schema</button> first.
          </p>
          <p class="hint" *ngIf="p.dataMode===1 && !(p.fieldMappings||[]).length">
            No fields selected. Open <button type="button" class="link" (click)="tab.set('connection')">Connection</button>,
            choose a table, then select the fields to use in the system.
          </p>

          <div *ngIf="dataTab()==='list'" class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th *ngFor="let f of fields()">{{ f.label || f.designField }}</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                <tr *ngFor="let r of records()" [class.sel]="selected()?.id===r.id" (click)="selectRecord(r)">
                  <td *ngFor="let f of fields()">
                    <ng-container *ngIf="isPhotoField(f); else textCell">
                      <img *ngIf="photoSrc(r, f) as src" [src]="src" class="thumb" alt="" />
                      <span *ngIf="!photoSrc(r, f)" class="muted">—</span>
                    </ng-container>
                    <ng-template #textCell>{{ r.data?.[f.designField] }}</ng-template>
                  </td>
                  <td class="actions">
                    <button type="button" class="secondary" *ngIf="r.id && !r.external" (click)="editRecord(r); $event.stopPropagation()">Edit</button>
                    <button type="button" class="link" *ngIf="r.id && !r.external" (click)="removeRecord(r); $event.stopPropagation()">Delete</button>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <form *ngIf="dataTab()==='form'" class="record-form" (ngSubmit)="saveRecord()">
            <h3>{{ editingId ? 'Edit record' : 'Add record' }}</h3>
            <div class="field" *ngFor="let f of fields(); let i = index">
              <label>{{ f.label || f.designField }} <span *ngIf="f.required">*</span></label>

              <ng-container *ngIf="!isPhotoField(f); else photoField">
                <input
                  [(ngModel)]="formData[f.designField]"
                  [name]="'f'+i"
                  [type]="inputType(f)"
                  [required]="!!f.required" />
              </ng-container>

              <ng-template #photoField>
                <div class="photo-box">
                  <img *ngIf="formPhotoPreview[f.designField] as prev" [src]="prev" class="preview-img" alt="" />
                  <input type="file" accept="image/*" (change)="onPhotoSelected($event, f)" [name]="'ph'+i" />
                  <small>{{ f.dataType === 'photo_binary' ? 'Stored in database (binary)' : 'Saved in project photos folder' }}</small>
                </div>
              </ng-template>
            </div>
            <div class="row">
              <button type="submit" class="primary" [disabled]="saving()">{{ saving() ? 'Saving...' : 'Save' }}</button>
              <button type="button" class="secondary" (click)="cancelForm()">Cancel</button>
            </div>
            <p class="error" *ngIf="formError()">{{ formError() }}</p>
          </form>
        </div>

        <aside class="preview-panel panel">
          <h3>Review</h3>
          <p class="hint" *ngIf="!selected()">Select a record to preview the card.</p>

          <details class="acc" [open]="true">
            <summary>Preview card front</summary>
            <div class="acc-body">
              <img *ngIf="previewFront()" [src]="apiBase + previewFront()" alt="Front" />
              <p *ngIf="!previewFront()" class="muted">No preview</p>
            </div>
          </details>

          <details class="acc" [open]="true">
            <summary>Preview card back</summary>
            <div class="acc-body">
              <img *ngIf="previewBack()" [src]="apiBase + previewBack()" alt="Back" />
              <p *ngIf="!previewBack()" class="muted">No preview</p>
            </div>
          </details>

          <button type="button" class="secondary" (click)="refreshPreview()" [disabled]="!selected()">Refresh preview</button>
        </aside>
      </section>

      <section *ngIf="tab()==='design'" class="panel">
        <app-designer
          [designJson]="p.designJson"
          [tableFields]="designerFields()"
          [assetBaseUrl]="apiBase"
          [saveHandler]="saveDesign"
          [imageUploadHandler]="uploadDesignImage"></app-designer>
      </section>

      <section *ngIf="tab()==='print'" class="panel">
        <div class="row">
          <select [(ngModel)]="printerId" name="printerId">
            <option value="">Select printer</option>
            <option *ngFor="let pr of printers()" [value]="pr.id">{{ pr.name }} {{ pr.isOnline ? '(online)' : '(offline)' }}</option>
          </select>
          <button class="primary" (click)="print()" [disabled]="!selected() || !printerId">Print selected</button>
        </div>
        <h3>Recent jobs</h3>
        <ul>
          <li *ngFor="let j of jobs()">{{ j.recordKey }} — {{ j.status }} <small>{{ j.createdAtUtc | date:'short' }}</small></li>
        </ul>
      </section>

      <section *ngIf="tab()==='connection'" class="panel">
        <h3>External database</h3>
        <label>Database type</label>
        <select [(ngModel)]="extDbType" name="extDbType">
          <option [ngValue]="1">SQL Server</option>
          <option [ngValue]="2">MySQL</option>
          <option [ngValue]="0">SQLite file</option>
        </select>
        <label>Connection string</label>
        <textarea [(ngModel)]="connectionString" name="cs" rows="3"></textarea>
        <div class="row">
          <button type="button" (click)="testConn()">Test</button>
          <button type="button" (click)="loadTables()">Discover tables</button>
        </div>
        <p *ngIf="connMsg()">{{ connMsg() }}</p>

        <label>Table / view</label>
        <div class="row">
          <input [(ngModel)]="tableName" name="table" placeholder="Select from list or type name" />
          <button type="button" class="secondary" (click)="loadColumns()" [disabled]="!tableName || !connectionString">Load columns</button>
        </div>
        <ul class="table-pick" *ngIf="tables().length">
          <li *ngFor="let t of tables()"
              [class.active]="tableName===t"
              (click)="selectTable(t)">{{ t }}</li>
        </ul>

        <div class="fields-pick" *ngIf="discoveredColumns.length">
          <div class="row fields-head">
            <h3>Fields to use in system</h3>
            <button type="button" class="link" (click)="selectAllColumns(true)">Select all</button>
            <button type="button" class="link" (click)="selectAllColumns(false)">Clear</button>
          </div>
          <p class="hint">Selected fields appear in Data list, Form, and Design bind field.</p>
          <table class="cols">
            <thead>
              <tr>
                <th>Use</th>
                <th>Column</th>
                <th>Label</th>
                <th>Type</th>
                <th>Key</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let col of discoveredColumns; let i = index">
                <td><input type="checkbox" [(ngModel)]="col.selected" [name]="'use'+i" /></td>
                <td><code>{{ col.name }}</code></td>
                <td><input [(ngModel)]="col.label" [name]="'el'+i" /></td>
                <td>
                  <select [(ngModel)]="col.dataType" [name]="'et'+i">
                    <option value="string">Text</option>
                    <option value="number">Number</option>
                    <option value="date">Date</option>
                    <option value="photo">Photo (file)</option>
                    <option value="photo_binary">Photo (binary)</option>
                  </select>
                </td>
                <td><input type="checkbox" [(ngModel)]="col.isKey" [name]="'ek'+i" (change)="onExternalKey(i)" /></td>
              </tr>
            </tbody>
          </table>
        </div>

        <div class="row">
          <button type="button" class="primary" (click)="saveConnection()">Save connection &amp; fields</button>
        </div>
        <p class="hint" *ngIf="(p.fieldMappings||[]).length">
          Currently saved: {{ (p.fieldMappings||[]).length }} field(s).
        </p>
      </section>
    </div>
  `,
  styles: [`
    .page { display: grid; gap: 1rem; }
    header { display: flex; justify-content: space-between; gap: 1rem; flex-wrap: wrap; align-items: end; }
    .sub { margin: .2rem 0 0; color: #567; }
    a { color: #1c4b73; text-decoration: none; }
    .tabs button, .subtabs button, .row button, .file, .link, .actions button {
      padding: .5rem .8rem; border-radius: 8px; border: 1px solid #c9d4e0; background: #fff; cursor: pointer;
    }
    .tabs .active, .subtabs .active, .primary { background: #1c4b73; color: #fff; border-color: #1c4b73; }
    .secondary { background: #e8eef5; color: #1c4b73; }
    .panel { background: #fff; border-radius: 12px; padding: 1rem; display: grid; gap: .75rem; }
    .data-layout { display: grid; grid-template-columns: minmax(0, 1fr) 320px; gap: 1rem; align-items: start; }
    .subtabs { display: flex; flex-wrap: wrap; gap: .45rem; align-items: center; }
    .spacer { flex: 1; }
    .row { display: flex; flex-wrap: wrap; gap: .5rem; align-items: center; }
    .hint, .muted { color: #678; }
    .cols, table { width: 100%; border-collapse: collapse; }
    .cols th, .cols td, th, td { text-align: left; padding: .45rem; border-bottom: 1px solid #eef2f6; vertical-align: middle; }
    tr.sel { background: #eef5fb; }
    .table-wrap { overflow: auto; }
    .record-form { display: grid; gap: .75rem; }
    .field { display: grid; gap: .3rem; }
    .field label { font-size: .85rem; color: #456; }
    .photo-box { display: grid; gap: .4rem; }
    .preview-img, .thumb { width: 72px; height: 72px; object-fit: cover; border-radius: 8px; border: 1px solid #ccd; background: #f4f7fa; }
    .preview-panel img { width: 100%; border: 1px solid #ccd; border-radius: 8px; background: #f8fafc; }
    .acc { border: 1px solid #e1e8f0; border-radius: 10px; padding: .4rem .6rem; }
    .acc summary { cursor: pointer; font-weight: 600; }
    .acc-body { padding: .6rem 0; }
    textarea, input, select { padding: .55rem .7rem; border: 1px solid #c9d4e0; border-radius: 8px; }
    .link { background: transparent; border: 0; color: #b00020; }
    .error { color: #b00020; margin: 0; }
    .actions { white-space: nowrap; display: flex; gap: .35rem; }
    .table-pick { list-style: none; margin: .5rem 0 0; padding: 0; display: flex; flex-wrap: wrap; gap: .35rem; max-height: 140px; overflow: auto; }
    .table-pick li {
      padding: .35rem .65rem; border: 1px solid #c9d4e0; border-radius: 8px; cursor: pointer; background: #fff; font-size: .85rem;
    }
    .table-pick li.active { background: #1c4b73; color: #fff; border-color: #1c4b73; }
    .fields-pick { margin-top: 1rem; display: grid; gap: .5rem; }
    .fields-head { align-items: center; }
    .fields-head h3 { margin: 0; flex: 1; }
    code { font-size: .85rem; }
    @media (max-width: 980px) { .data-layout { grid-template-columns: 1fr; } }
  `]
})
export class ProjectDetailComponent implements OnInit {
  project = signal<any>(null);
  records = signal<any[]>([]);
  printers = signal<any[]>([]);
  jobs = signal<any[]>([]);
  selected = signal<any>(null);
  fields = signal<FieldDef[]>([]);
  designerFields = signal<{ name: string; label?: string; dataType?: string }[]>([]);
  tab = signal<'data' | 'schema' | 'design' | 'print' | 'connection'>('data');
  dataTab = signal<'list' | 'form'>('list');
  tables = signal<string[]>([]);
  connMsg = signal('');
  schemaMsg = signal('');
  formError = signal('');
  saving = signal(false);
  previewFront = signal<string | null>(null);
  previewBack = signal<string | null>(null);
  search = '';
  printerId = '';
  editingId: string | null = null;
  formData: Record<string, any> = {};
  formPhotoFiles: Record<string, File | null> = {};
  formPhotoPreview: Record<string, string> = {};
  connectionString = '';
  tableName = '';
  schemaTableName = '';
  schemaFields: FieldDef[] = [];
  discoveredColumns: DiscoveredColumn[] = [];
  extDbType = 1;
  apiBase = environment.apiUrl;
  private id = '';

  saveDesign = (json: string) => {
    const p = this.project();
    if (!p) return;
    this.api.updateProject(this.id, { ...this.toUpdateBody(p), designJson: json })
      .subscribe(updated => this.applyProject(updated));
  };

  uploadDesignImage = async (file: File): Promise<string> => {
    const res = await firstValueFrom(this.api.uploadDesignAsset(this.id, file));
    return res.url;
  };

  constructor(private route: ActivatedRoute, private api: ApiService) {}

  ngOnInit() {
    this.id = this.route.snapshot.paramMap.get('id') || '';
    this.api.getProject(this.id).subscribe(p => {
      this.applyProject(p);
      this.extDbType = p.externalDatabaseType ?? 1;
      this.tableName = p.externalTableName || '';
      this.loadRecords();
      if (p.dataMode === 0 && !(p.fieldMappings || []).length) this.tab.set('schema');
      if (p.dataMode === 1 && !(p.fieldMappings || []).length) this.tab.set('connection');
      if (p.dataMode === 1 && p.externalTableName && p.hasConnectionString) {
        // Prefill column picker from saved mappings when connection secret is already stored.
        this.discoveredColumns = (p.fieldMappings || []).map((f: FieldDef) => ({
          name: f.sourceColumn || f.designField,
          rawType: f.dataType || 'string',
          label: f.label || f.sourceColumn || f.designField,
          dataType: f.dataType || 'string',
          selected: true,
          isKey: !!f.isKey
        }));
      }
    });
    this.api.getPrinters().subscribe(list => this.printers.set(list));
    this.api.getJobs(this.id).subscribe(list => this.jobs.set(list));
  }

  applyProject(p: any) {
    this.project.set(p);
    const mappings = (p.fieldMappings || []) as FieldDef[];
    this.fields.set(mappings);
    this.designerFields.set(mappings.map(f => ({
      name: f.designField || f.sourceColumn,
      label: f.label || f.designField || f.sourceColumn,
      dataType: f.dataType || (f.isPhoto ? 'photo' : 'string')
    })));
    this.schemaTableName = p.internalTableName || p.name || '';
    this.schemaFields = mappings.map((f: FieldDef) => ({
      designField: f.designField || f.sourceColumn,
      sourceColumn: f.sourceColumn || f.designField,
      label: f.label || f.designField,
      dataType: f.dataType || (f.isPhoto ? 'photo' : 'string'),
      isPhoto: !!f.isPhoto || f.dataType === 'photo' || f.dataType === 'photo_binary',
      isKey: !!f.isKey,
      required: !!f.required
    }));
  }

  loadRecords() {
    this.api.getRecords(this.id, this.search).subscribe(list => this.records.set(list));
  }

  isPhotoField(f: FieldDef) {
    return f.dataType === 'photo' || f.dataType === 'photo_binary' || !!f.isPhoto;
  }

  photoSrc(r: any, f: FieldDef): string | null {
    const fromApi = r.photos?.[f.designField];
    if (fromApi) return fromApi.startsWith('http') ? fromApi : this.apiBase + fromApi;
    const val = r.data?.[f.designField];
    if (typeof val === 'string' && val.startsWith('/files/')) return this.apiBase + val;
    return null;
  }

  selectRecord(r: any) {
    this.selected.set(r);
    this.refreshPreview();
  }

  openNew() {
    this.editingId = null;
    this.formData = {};
    this.formPhotoFiles = {};
    this.formPhotoPreview = {};
    for (const f of this.fields()) {
      if (!this.isPhotoField(f)) this.formData[f.designField] = '';
    }
    this.formError.set('');
    this.dataTab.set('form');
  }

  editRecord(r: any) {
    this.editingId = r.id;
    this.selected.set(r);
    this.formData = { ...(r.data || {}) };
    this.formPhotoFiles = {};
    this.formPhotoPreview = {};
    for (const f of this.fields()) {
      if (this.isPhotoField(f)) {
        const src = this.photoSrc(r, f);
        if (src) this.formPhotoPreview[f.designField] = src;
      }
    }
    this.formError.set('');
    this.dataTab.set('form');
    this.refreshPreview();
  }

  cancelForm() {
    this.dataTab.set('list');
    this.editingId = null;
    this.formError.set('');
  }

  onPhotoSelected(ev: Event, f: FieldDef) {
    const file = (ev.target as HTMLInputElement).files?.[0];
    if (!file) return;
    this.formPhotoFiles[f.designField] = file;
    this.formPhotoPreview[f.designField] = URL.createObjectURL(file);
  }

  async saveRecord() {
    this.saving.set(true);
    this.formError.set('');
    const keyField = this.fields().find(f => f.isKey)?.designField || this.fields()[0]?.designField;
    const key = keyField ? String(this.formData[keyField] ?? '') : undefined;
    const data: Record<string, any> = {};
    for (const f of this.fields()) {
      if (this.isPhotoField(f)) continue;
      data[f.designField] = this.formData[f.designField];
    }

    const req$ = this.editingId
      ? this.api.updateRecord(this.id, this.editingId, { key, data })
      : this.api.createRecord(this.id, { key, data });

    req$.subscribe({
      next: async (saved) => {
        const recordId = saved.id || this.editingId;
        try {
          if (recordId) {
            for (const f of this.fields()) {
              const file = this.formPhotoFiles[f.designField];
              if (!file) continue;
              const storage = f.dataType === 'photo_binary' ? 'binary' : 'file';
              await firstValueFrom(this.api.uploadRecordPhoto(this.id, recordId, f.designField, file, storage));
            }
          }
          this.saving.set(false);
          this.dataTab.set('list');
          this.loadRecords();
          if (recordId) {
            // refresh selection/preview after uploads
            setTimeout(() => {
              const found = this.records().find(r => r.id === recordId);
              if (found) this.selectRecord(found);
              else this.api.getRecords(this.id).subscribe(list => {
                this.records.set(list);
                const r = list.find((x: any) => x.id === recordId);
                if (r) this.selectRecord(r);
              });
            }, 200);
          }
        } catch (e: any) {
          this.saving.set(false);
          this.formError.set(e?.error?.message || 'Photo upload failed');
        }
      },
      error: (e) => {
        this.saving.set(false);
        this.formError.set(e?.error?.message || 'Save failed');
      }
    });
  }

  removeRecord(r: any) {
    if (!r.id || !confirm('Delete record?')) return;
    this.api.deleteRecord(this.id, r.id).subscribe(() => {
      if (this.selected()?.id === r.id) {
        this.selected.set(null);
        this.previewFront.set(null);
        this.previewBack.set(null);
      }
      this.loadRecords();
    });
  }

  onCsv(ev: Event) {
    const file = (ev.target as HTMLInputElement).files?.[0];
    if (!file) return;
    this.api.importCsv(this.id, file).subscribe(() => this.loadRecords());
  }

  refreshPreview() {
    const sel = this.selected();
    if (!sel) {
      this.previewFront.set(null);
      this.previewBack.set(null);
      return;
    }
    const apply = (urls: any) => {
      const bust = urls.cacheBust ? `?t=${urls.cacheBust}` : `?t=${Date.now()}`;
      this.previewFront.set(urls.frontImage ? urls.frontImage + bust : null);
      this.previewBack.set(urls.backImage ? urls.backImage + bust : null);
    };
    if (sel.external || !sel.id) {
      this.api.previewWithData(this.id, sel.data || {}).subscribe(apply);
    } else {
      this.api.preview(this.id, sel.id).subscribe(apply);
    }
  }

  addSchemaField() {
    this.schemaFields.push({
      designField: '', sourceColumn: '', label: '', dataType: 'string', isKey: false, required: false
    });
  }

  onTypeChange(col: FieldDef, value: string) {
    col.dataType = value;
    col.isPhoto = value === 'photo' || value === 'photo_binary';
  }

  onSchemaKey(i: number) {
    if (!this.schemaFields[i].isKey) return;
    this.schemaFields.forEach((f, idx) => {
      if (idx !== i) f.isKey = false;
      else f.required = true;
    });
  }

  saveSchema() {
    const p = this.project();
    if (!p) return;
    const fieldMappings = this.schemaFields
      .filter(f => (f.sourceColumn || f.designField || '').trim())
      .map(f => {
        const name = (f.sourceColumn || f.designField).trim();
        return {
          designField: name,
          sourceColumn: name,
          label: (f.label || name).trim(),
          dataType: f.dataType || 'string',
          isPhoto: f.dataType === 'photo' || f.dataType === 'photo_binary',
          isKey: !!f.isKey,
          required: !!f.required || !!f.isKey
        };
      });

    this.api.updateProject(this.id, {
      ...this.toUpdateBody(p),
      internalTableName: this.schemaTableName,
      fieldMappings
    }).subscribe({
      next: updated => {
        this.applyProject(updated);
        this.schemaMsg.set('Schema saved');
        this.tab.set('data');
      },
      error: e => this.schemaMsg.set(e?.error?.message || 'Save failed')
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
    if (!this.connectionString) {
      this.connMsg.set('Enter a connection string first');
      return;
    }
    this.api.testConnection({ databaseType: this.extDbType, connectionString: this.connectionString })
      .subscribe({
        next: r => this.connMsg.set(r.success ? 'Connection OK' : 'Failed'),
        error: e => this.connMsg.set(e?.error?.message || 'Failed')
      });
  }

  loadTables() {
    if (!this.connectionString) {
      this.connMsg.set('Enter a connection string first');
      return;
    }
    this.api.discoverTables({ databaseType: this.extDbType, connectionString: this.connectionString })
      .subscribe({
        next: t => {
          this.tables.set(t);
          this.connMsg.set(t.length ? `${t.length} table(s) found` : 'No tables found');
        },
        error: e => this.connMsg.set(e?.error?.message || 'Discover tables failed')
      });
  }

  selectTable(name: string) {
    this.tableName = name;
    this.loadColumns();
  }

  loadColumns() {
    if (!this.connectionString || !this.tableName) {
      this.connMsg.set('Connection string and table are required');
      return;
    }
    const existing = new Map(
      (this.project()?.fieldMappings || []).map((f: FieldDef) => [
        (f.sourceColumn || f.designField || '').toLowerCase(),
        f
      ])
    );
    this.api.discoverColumns({
      databaseType: this.extDbType,
      connectionString: this.connectionString,
      tableName: this.tableName
    }).subscribe({
      next: cols => {
        this.discoveredColumns = (cols || []).map((c: any) => {
          const name = c.name || c.Name || '';
          const prev = existing.get(name.toLowerCase()) as FieldDef | undefined;
          return {
            name,
            rawType: c.dataType || c.DataType || '',
            label: prev?.label || name,
            dataType: prev?.dataType || this.mapSqlType(c.dataType || c.DataType || ''),
            selected: existing.size ? !!prev : true,
            isKey: prev ? !!prev.isKey : false
          } as DiscoveredColumn;
        });
        if (!this.discoveredColumns.some(c => c.isKey) && this.discoveredColumns.length) {
          this.discoveredColumns[0].isKey = true;
        }
        this.connMsg.set(`${this.discoveredColumns.length} column(s) loaded — select fields to use`);
      },
      error: e => this.connMsg.set(e?.error?.message || 'Load columns failed')
    });
  }

  selectAllColumns(on: boolean) {
    for (const c of this.discoveredColumns) c.selected = on;
  }

  onExternalKey(i: number) {
    if (!this.discoveredColumns[i].isKey) return;
    this.discoveredColumns.forEach((c, idx) => {
      if (idx !== i) c.isKey = false;
    });
  }

  mapSqlType(sqlType: string) {
    const s = (sqlType || '').toLowerCase();
    if (/(int|decimal|numeric|float|real|money|double|number)/.test(s)) return 'number';
    if (/(date|time)/.test(s)) return 'date';
    if (/(image|binary|blob|varbinary|bytea)/.test(s)) return 'photo_binary';
    return 'string';
  }

  saveConnection() {
    const p = this.project();
    if (!p) return;
    if (!this.tableName.trim()) {
      this.connMsg.set('Select or enter a table name');
      return;
    }

    let fieldMappings = this.discoveredColumns
      .filter(c => c.selected && c.name.trim())
      .map(c => ({
        designField: c.name.trim(),
        sourceColumn: c.name.trim(),
        label: (c.label || c.name).trim(),
        dataType: c.dataType || 'string',
        isPhoto: c.dataType === 'photo' || c.dataType === 'photo_binary',
        isKey: !!c.isKey,
        required: !!c.isKey
      }));

    // If columns were not reloaded this session, keep previously saved mappings.
    if (!fieldMappings.length && (p.fieldMappings || []).length && !this.discoveredColumns.length) {
      fieldMappings = p.fieldMappings;
    }

    if (!fieldMappings.length) {
      this.connMsg.set('Load columns and select at least one field');
      return;
    }

    if (!fieldMappings.some(f => f.isKey)) {
      fieldMappings[0].isKey = true;
      fieldMappings[0].required = true;
    }

    this.api.updateProject(this.id, {
      ...this.toUpdateBody(p),
      dataMode: 1,
      externalDatabaseType: this.extDbType,
      connectionString: this.connectionString || undefined,
      externalTableName: this.tableName.trim(),
      fieldMappings
    }).subscribe({
      next: updated => {
        this.applyProject(updated);
        this.connMsg.set(`Saved — ${fieldMappings.length} field(s) available in Data and Design`);
        this.loadRecords();
      },
      error: e => this.connMsg.set(e?.error?.message || 'Save failed')
    });
  }

  inputType(f: FieldDef) {
    if (f.dataType === 'number') return 'number';
    if (f.dataType === 'date') return 'date';
    return 'text';
  }

  private toUpdateBody(p: any) {
    return {
      name: p.name,
      description: p.description,
      cardSize: p.cardSize,
      dualSided: p.dualSided,
      dataMode: p.dataMode,
      internalTableName: p.internalTableName,
      externalDatabaseType: p.externalDatabaseType,
      externalTableName: p.externalTableName,
      allowExternalWriteBack: p.allowExternalWriteBack,
      fieldMappings: p.fieldMappings,
      defaultPrinterId: p.defaultPrinterId
    };
  }
}
