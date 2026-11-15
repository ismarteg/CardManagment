import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private base = environment.apiUrl;

  constructor(private http: HttpClient) {}

  getProjects() { return this.http.get<any[]>(`${this.base}/api/projects`); }
  getProject(id: string) { return this.http.get<any>(`${this.base}/api/projects/${id}`); }
  createProject(body: any) { return this.http.post<any>(`${this.base}/api/projects`, body); }
  updateProject(id: string, body: any) { return this.http.put<any>(`${this.base}/api/projects/${id}`, body); }
  deleteProject(id: string) { return this.http.delete(`${this.base}/api/projects/${id}`); }
  testConnection(body: any) { return this.http.post<any>(`${this.base}/api/projects/test-connection`, body); }
  discoverTables(body: any) { return this.http.post<string[]>(`${this.base}/api/projects/discover/tables`, body); }
  discoverColumns(body: any) { return this.http.post<any[]>(`${this.base}/api/projects/discover/columns`, body); }

  getRecords(projectId: string, search = '') {
    let params = new HttpParams();
    if (search) params = params.set('search', search);
    return this.http.get<any[]>(`${this.base}/api/projects/${projectId}/records`, { params });
  }
  createRecord(projectId: string, body: any) { return this.http.post<any>(`${this.base}/api/projects/${projectId}/records`, body); }
  updateRecord(projectId: string, recordId: string, body: any) { return this.http.put<any>(`${this.base}/api/projects/${projectId}/records/${recordId}`, body); }
  deleteRecord(projectId: string, recordId: string) { return this.http.delete(`${this.base}/api/projects/${projectId}/records/${recordId}`); }
  uploadRecordPhoto(projectId: string, recordId: string, fieldName: string, file: File, storage: 'file' | 'binary') {
    const fd = new FormData();
    fd.append('file', file);
    return this.http.post<any>(
      `${this.base}/api/projects/${projectId}/records/${recordId}/photos/${encodeURIComponent(fieldName)}?storage=${storage}`,
      fd
    );
  }
  uploadDesignAsset(projectId: string, file: File) {
    const fd = new FormData();
    fd.append('file', file);
    return this.http.post<{ url: string; fileName: string }>(
      `${this.base}/api/projects/${projectId}/design-assets`,
      fd
    );
  }
  photoUrl(projectId: string, recordId: string, fieldName: string) {
    return `${this.base}/api/projects/${projectId}/records/${recordId}/photos/${encodeURIComponent(fieldName)}`;
  }
  importCsv(projectId: string, file: File) {
    const fd = new FormData();
    fd.append('file', file);
    return this.http.post(`${this.base}/api/projects/${projectId}/records/import-csv`, fd);
  }

  getPrinters() { return this.http.get<any[]>(`${this.base}/api/printers`); }
  createPrinter(body: any) { return this.http.post(`${this.base}/api/printers`, body); }
  createPairingCode() { return this.http.post<any>(`${this.base}/api/agents/pairing-codes`, {}); }
  getAgents() { return this.http.get<any[]>(`${this.base}/api/agents`); }

  queuePrint(body: any) { return this.http.post(`${this.base}/api/print/queue`, body); }
  getJobs(projectId?: string) {
    let params = new HttpParams();
    if (projectId) params = params.set('projectId', projectId);
    return this.http.get<any[]>(`${this.base}/api/print/jobs`, { params });
  }
  preview(projectId: string, recordId?: string) {
    let params = new HttpParams();
    if (recordId) params = params.set('recordId', recordId);
    return this.http.get<any>(`${this.base}/api/print/preview/${projectId}`, { params });
  }
  previewWithData(projectId: string, data: Record<string, any>) {
    return this.http.post<any>(`${this.base}/api/print/preview/${projectId}`, { data });
  }

  getAudit(paramsObj: Record<string, string>) {
    let params = new HttpParams();
    Object.entries(paramsObj).forEach(([k, v]) => { if (v) params = params.set(k, v); });
    return this.http.get<any>(`${this.base}/api/audit`, { params });
  }
  getSsoSettings() { return this.http.get<any>(`${this.base}/api/settings/sso`); }
  saveSsoSettings(body: any) { return this.http.put(`${this.base}/api/settings/sso`, body); }
  ssoStatus() { return this.http.get<any>(`${this.base}/api/auth/sso/status`); }
}
