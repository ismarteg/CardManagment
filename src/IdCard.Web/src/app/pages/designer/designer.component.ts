import { Component, ElementRef, Input, OnChanges, ViewChild, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

export interface DesignElement {
  id: string;
  type: string;
  field?: string;
  text?: string;
  src?: string;
  x: number;
  y: number;
  w: number;
  h: number;
  fontSize: number;
  fontFamily: string;
  color: string;
  align: string;
  zIndex: number;
}

export interface CardDesign {
  designVersion: number;
  front: { backgroundColor?: string; backgroundImage?: string; elements: DesignElement[] };
  back: { backgroundColor?: string; backgroundImage?: string; elements: DesignElement[] };
}

@Component({
  selector: 'app-designer',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="designer">
      <div class="toolbar">
        <button type="button" (click)="side.set('front')" [class.active]="side()==='front'">Front</button>
        <button type="button" (click)="side.set('back')" [class.active]="side()==='back'">Back</button>
        <button type="button" (click)="add('text')">Text</button>
        <button type="button" (click)="add('photo')">Photo</button>
        <button type="button" (click)="add('image')">Image</button>
        <button type="button" (click)="add('qr')">QR</button>
        <button type="button" (click)="add('rect')">Rect</button>
        <button type="button" class="primary" (click)="emitSave()">Save design</button>
      </div>

      <div class="workspace">
        <div class="canvas-wrap" #wrap
             (mousemove)="onMove($event)" (mouseup)="endDrag()" (mouseleave)="endDrag()">
          <div class="canvas" [style.background]="currentSide().backgroundColor || '#fff'">
            <div class="el" *ngFor="let el of currentSide().elements"
                 [style.left.px]="el.x * scale"
                 [style.top.px]="el.y * scale"
                 [style.width.px]="el.w * scale"
                 [style.height.px]="el.h * scale"
                 [class.selected]="selected()?.id === el.id"
                 (mousedown)="startDrag($event, el)"
                 (click)="selected.set(el)">
              <ng-container [ngSwitch]="el.type">
                <span *ngSwitchCase="'text'" [style.fontSize.px]="el.fontSize * scale" [style.color]="el.color">
                  {{ el.field ? '{{' + el.field + '}}' : el.text }}
                </span>
                <div *ngSwitchCase="'photo'" class="placeholder">Photo: {{ el.field || 'Photo' }}</div>
                <div *ngSwitchCase="'image'" class="placeholder">Image</div>
                <div *ngSwitchCase="'qr'" class="placeholder">QR {{ el.field }}</div>
                <div *ngSwitchCase="'rect'" class="rect" [style.background]="el.color"></div>
              </ng-container>
            </div>
          </div>
        </div>

        <aside *ngIf="selected() as el">
          <h4>Properties</h4>
          <label>Type</label><input [value]="el.type" disabled />
          <label>Field</label><input [(ngModel)]="el.field" name="field" />
          <label>Text</label><input [(ngModel)]="el.text" name="text" />
          <label>X</label><input type="number" [(ngModel)]="el.x" name="x" />
          <label>Y</label><input type="number" [(ngModel)]="el.y" name="y" />
          <label>W</label><input type="number" [(ngModel)]="el.w" name="w" />
          <label>H</label><input type="number" [(ngModel)]="el.h" name="h" />
          <label>Font size</label><input type="number" [(ngModel)]="el.fontSize" name="fs" />
          <label>Color</label><input [(ngModel)]="el.color" name="color" />
          <button type="button" class="danger" (click)="remove(el)">Delete</button>
        </aside>
      </div>
    </div>
  `,
  styles: [`
    .designer { display: grid; gap: .75rem; }
    .toolbar { display: flex; flex-wrap: wrap; gap: .4rem; }
    .toolbar button { padding: .45rem .7rem; border-radius: 8px; border: 1px solid #c9d4e0; background: #fff; cursor: pointer; }
    .toolbar button.active, .toolbar .primary { background: #1c4b73; color: #fff; border-color: #1c4b73; }
    .workspace { display: grid; grid-template-columns: 1fr 260px; gap: 1rem; }
    .canvas-wrap { overflow: auto; background: #d9e2ec; border-radius: 12px; padding: 1rem; }
    .canvas { position: relative; width: 506px; height: 319px; border: 1px solid #99a; box-shadow: 0 10px 30px rgba(0,0,0,.12); }
    .el { position: absolute; border: 1px dashed transparent; cursor: move; overflow: hidden; user-select: none; }
    .el.selected { border-color: #1c4b73; background: rgba(28,75,115,.06); }
    .placeholder { width: 100%; height: 100%; display: grid; place-items: center; background: #eef2f6; font-size: .75rem; color: #567; }
    .rect { width: 100%; height: 100%; }
    aside { background: #fff; border-radius: 12px; padding: 1rem; display: grid; gap: .35rem; align-content: start; }
    aside input { padding: .45rem; border: 1px solid #c9d4e0; border-radius: 6px; }
    .danger { background: #b00020; color: #fff; border: 0; padding: .5rem; border-radius: 8px; cursor: pointer; }
    @media (max-width: 900px) { .workspace { grid-template-columns: 1fr; } }
  `]
})
export class DesignerComponent implements OnChanges {
  @Input() designJson = '';
  @Input() saveHandler?: (json: string) => void;
  @ViewChild('wrap') wrap?: ElementRef<HTMLDivElement>;

  design = signal<CardDesign>(this.empty());
  side = signal<'front' | 'back'>('front');
  selected = signal<DesignElement | null>(null);
  scale = 0.5;
  private dragging: DesignElement | null = null;
  private offsetX = 0;
  private offsetY = 0;

  ngOnChanges() {
    try {
      this.design.set(JSON.parse(this.designJson || '{}'));
    } catch {
      this.design.set(this.empty());
    }
    if (!this.design().front) this.design.update(d => ({ ...d, front: { elements: [] } }));
    if (!this.design().back) this.design.update(d => ({ ...d, back: { elements: [] } }));
  }

  currentSide() {
    return this.side() === 'front' ? this.design().front : this.design().back;
  }

  add(type: string) {
    const el: DesignElement = {
      id: crypto.randomUUID(),
      type,
      field: type === 'photo' ? 'Photo' : type === 'text' ? 'FullName' : type === 'qr' ? 'StudentId' : undefined,
      text: type === 'text' ? 'Sample' : undefined,
      x: 40, y: 40, w: type === 'photo' ? 120 : 160, h: type === 'photo' ? 150 : 36,
      fontSize: 18, fontFamily: 'Arial', color: type === 'rect' ? '#1c4b73' : '#111111',
      align: 'left', zIndex: this.currentSide().elements.length + 1
    };
    this.currentSide().elements.push(el);
    this.selected.set(el);
  }

  remove(el: DesignElement) {
    const side = this.currentSide();
    side.elements = side.elements.filter(e => e.id !== el.id);
    this.selected.set(null);
  }

  startDrag(ev: MouseEvent, el: DesignElement) {
    this.dragging = el;
    this.offsetX = ev.offsetX;
    this.offsetY = ev.offsetY;
    ev.preventDefault();
  }

  onMove(ev: MouseEvent) {
    if (!this.dragging || !this.wrap) return;
    const rect = this.wrap.nativeElement.querySelector('.canvas')!.getBoundingClientRect();
    this.dragging.x = Math.max(0, (ev.clientX - rect.left - this.offsetX) / this.scale);
    this.dragging.y = Math.max(0, (ev.clientY - rect.top - this.offsetY) / this.scale);
  }

  endDrag() { this.dragging = null; }

  emitSave() {
    this.saveHandler?.(JSON.stringify(this.design()));
  }

  private empty(): CardDesign {
    return { designVersion: 1, front: { backgroundColor: '#FFFFFF', elements: [] }, back: { backgroundColor: '#FFFFFF', elements: [] } };
  }
}
