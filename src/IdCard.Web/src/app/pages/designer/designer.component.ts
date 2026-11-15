import { Component, ElementRef, HostListener, Input, OnChanges, OnDestroy, ViewChild, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import QRCode from 'qrcode';

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
  /** ltr | rtl */
  direction?: string;
  zIndex: number;
  autoSize?: boolean;
  wrapText?: boolean;
  backgroundColor?: string;
  /** 0–1 for image/photo transparency */
  opacity?: number;
}

export interface CustomFontDefinition {
  family: string;
  dataBase64: string;
  format: string;
}

export interface TableFieldOption {
  name: string;
  label?: string;
  dataType?: string;
}

export interface CardDesign {
  designVersion: number;
  fonts?: CustomFontDefinition[];
  front: { backgroundColor?: string; backgroundImage?: string; elements: DesignElement[] };
  back: { backgroundColor?: string; backgroundImage?: string; elements: DesignElement[] };
}

type ResizeHandle = 'nw' | 'n' | 'ne' | 'e' | 'se' | 's' | 'sw' | 'w';

const BUILTIN_FONTS = ['Arial', 'Segoe UI', 'Tahoma', 'Times New Roman', 'Courier New'];

@Component({
  selector: 'app-designer',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="designer">
      <div class="toolbar">
        <button type="button" (click)="setSide('front')" [class.active]="side()==='front'">Front</button>
        <button type="button" (click)="setSide('back')" [class.active]="side()==='back'">Back</button>
        <span class="sep"></span>
        <button type="button" (click)="add('text')">Text</button>
        <button type="button" (click)="add('photo')">Photo</button>
        <button type="button" (click)="add('image')">Image</button>
        <button type="button" (click)="add('qr')">QR</button>
        <button type="button" (click)="add('rect')">Rect</button>
        <span class="sep"></span>
        <button type="button" title="Undo (Ctrl+Z)" (click)="undo()" [disabled]="!canUndo()">Undo</button>
        <button type="button" title="Redo (Ctrl+Y)" (click)="redo()" [disabled]="!canRedo()">Redo</button>
        <span class="sep"></span>
        <label class="inline">
          Card BG
          <input type="color" [ngModel]="currentSide().backgroundColor || '#ffffff'" (ngModelChange)="setCardBg($event)" name="cardBg" />
        </label>
        <span class="sep"></span>
        <label class="inline check">
          <input type="checkbox" [ngModel]="showGrid()" (ngModelChange)="showGrid.set($event)" name="showGrid" />
          Grid
        </label>
        <label class="inline check">
          <input type="checkbox" [ngModel]="snapToGrid()" (ngModelChange)="snapToGrid.set($event)" name="snapGrid" [disabled]="!showGrid()" />
          Snap
        </label>
        <label class="inline">
          Size
          <select [ngModel]="gridSize()" (ngModelChange)="gridSize.set(+$event)" name="gridSize">
            <option [ngValue]="10">10</option>
            <option [ngValue]="20">20</option>
            <option [ngValue]="25">25</option>
            <option [ngValue]="50">50</option>
          </select>
        </label>
        <button type="button" class="primary" (click)="emitSave()">Save design</button>
      </div>

      <div class="workspace">
        <aside class="left-panel">
          <h4>Elements</h4>
          <p class="hint" *ngIf="!currentSide().elements.length">No elements yet. Use the toolbar to add.</p>
          <ul class="el-list">
            <li *ngFor="let el of sortedElements()"
                [class.active]="selected()?.id === el.id"
                (click)="select(el)">
              <div class="el-title">
                <strong>{{ el.type }}</strong>
                <small>{{ el.field || el.text || '—' }}</small>
              </div>
              <div class="el-actions" (click)="$event.stopPropagation()">
                <button type="button" title="Up" (click)="moveZ(el, 1)">↑</button>
                <button type="button" title="Down" (click)="moveZ(el, -1)">↓</button>
                <button type="button" class="danger-lite" title="Delete" (click)="remove(el)">×</button>
              </div>
            </li>
          </ul>

          <h4 class="fonts-title">Fonts</h4>
          <label class="font-upload">
            Add font from device
            <input type="file" accept=".ttf,.otf,.woff,.woff2" (change)="onFontFile($event)" />
          </label>
          <ul class="font-list" *ngIf="customFonts().length">
            <li *ngFor="let f of customFonts()">
              <span>{{ f.family }}</span>
              <button type="button" class="danger-lite" title="Remove" (click)="removeFont(f.family)">×</button>
            </li>
          </ul>
        </aside>

        <div class="canvas-wrap" #wrap
             (mousemove)="onPointerMove($event)"
             (mouseup)="endInteraction()"
             (mouseleave)="endInteraction()">
          <div class="canvas"
               [style.backgroundColor]="currentSide().backgroundColor || '#fff'"
               [class.grid-on]="showGrid()"
               [style.--grid]="(gridSize() * scale) + 'px'">
            <div class="grid-overlay" *ngIf="showGrid()" aria-hidden="true"></div>
            <div class="el" *ngFor="let el of sortedElements()"
                 [style.left.px]="el.x * scale"
                 [style.top.px]="el.y * scale"
                 [style.width.px]="el.w * scale"
                 [style.height.px]="el.h * scale"
                 [style.zIndex]="(el.zIndex || 0) + 1"
                 [style.background]="el.backgroundColor || 'transparent'"
                 [class.selected]="selected()?.id === el.id"
                 (mousedown)="startDrag($event, el)"
                 (click)="select(el)">
              <div class="el-body">
                <ng-container [ngSwitch]="el.type">
                  <div *ngSwitchCase="'text'" class="text-el"
                       [class.wrap]="el.wrapText"
                       [class.ta-left]="(el.align || 'left') === 'left'"
                       [class.ta-center]="el.align === 'center'"
                       [class.ta-right]="el.align === 'right'"
                       [class.dir-rtl]="(el.direction || 'ltr') === 'rtl'"
                       [class.dir-ltr]="(el.direction || 'ltr') !== 'rtl'"
                       [style.fontSize.px]="el.fontSize * scale"
                       [style.color]="el.color"
                       [style.fontFamily]="quotedFont(el.fontFamily)"
                       [attr.dir]="el.direction || 'ltr'">
                    {{ displayText(el) }}
                  </div>
                  <div *ngSwitchCase="'photo'" class="placeholder" [style.opacity]="el.opacity ?? 1">Photo: {{ el.field || 'Photo' }}</div>
                  <div *ngSwitchCase="'image'" class="img-el" [style.opacity]="el.opacity ?? 1">
                    <img *ngIf="el.src" [src]="resolveAssetUrl(el.src)" alt="" />
                    <span *ngIf="!el.src" class="placeholder">Image</span>
                  </div>
                  <div *ngSwitchCase="'qr'" class="qr-el">
                    <img *ngIf="qrPreview(el) as src; else qrEmpty" [src]="src" alt="QR" />
                    <ng-template #qrEmpty><span class="placeholder">QR</span></ng-template>
                  </div>
                  <div *ngSwitchCase="'rect'" class="rect" [style.background]="el.color"></div>
                </ng-container>
              </div>

              <ng-container *ngIf="selected()?.id === el.id">
                <span class="handle nw" (mousedown)="startResize($event, el, 'nw')"></span>
                <span class="handle n"  (mousedown)="startResize($event, el, 'n')"></span>
                <span class="handle ne" (mousedown)="startResize($event, el, 'ne')"></span>
                <span class="handle e"  (mousedown)="startResize($event, el, 'e')"></span>
                <span class="handle se" (mousedown)="startResize($event, el, 'se')"></span>
                <span class="handle s"  (mousedown)="startResize($event, el, 's')"></span>
                <span class="handle sw" (mousedown)="startResize($event, el, 'sw')"></span>
                <span class="handle w"  (mousedown)="startResize($event, el, 'w')"></span>
              </ng-container>
            </div>
          </div>
        </div>

        <aside class="props" *ngIf="selected() as el; else noSel">
          <h4>Properties</h4>

          <details class="acc" open>
            <summary>Content</summary>
            <div class="acc-body">
              <label>Type</label>
              <input [value]="el.type" disabled />

              <label>Bind field</label>
              <select [(ngModel)]="el.field" name="field" (ngModelChange)="onFieldPicked(el, $event)">
                <option [ngValue]="undefined">— None / static —</option>
                <option *ngFor="let f of tableFields" [ngValue]="f.name">
                  {{ f.label || f.name }}{{ f.dataType ? ' (' + f.dataType + ')' : '' }}
                </option>
              </select>
              <p class="hint" *ngIf="!tableFields?.length">No table fields. Define schema first.</p>

              <label *ngIf="el.type==='text' || el.type==='qr'">Static text</label>
              <input *ngIf="el.type==='text' || el.type==='qr'"
                     [(ngModel)]="el.text"
                     name="text"
                     [disabled]="!!el.field"
                     (ngModelChange)="onTextChanged(el)" />

              <label *ngIf="el.type==='image'">Image src</label>
              <input *ngIf="el.type==='image'" [(ngModel)]="el.src" name="src" (ngModelChange)="onPropChanged()" />
              <label *ngIf="el.type==='image'" class="font-upload">
                Upload image
                <input type="file" accept="image/*" (change)="onImageFile($event, el)" />
              </label>
            </div>
          </details>

          <details class="acc" open *ngIf="el.type==='image' || el.type==='photo'">
            <summary>Transparency</summary>
            <div class="acc-body">
              <label class="check">
                <input type="checkbox"
                       [ngModel]="isFullyTransparent(el)"
                       (ngModelChange)="setFullyTransparent(el, $event)"
                       name="transparent" />
                Transparent
              </label>
              <label>Opacity {{ opacityPct(el) }}%</label>
              <input type="range" min="0" max="100" step="1"
                     [ngModel]="opacityPct(el)"
                     (ngModelChange)="setOpacityPct(el, $event)"
                     [disabled]="isFullyTransparent(el)"
                     name="opacity" />
            </div>
          </details>

          <details class="acc" open>
            <summary>Align on card</summary>
            <div class="acc-body">
              <div class="align-grid">
                <button type="button" title="Left" (click)="alignElement(el, 'left')">⫷</button>
                <button type="button" title="Center H" (click)="alignElement(el, 'centerH')">⬌</button>
                <button type="button" title="Right" (click)="alignElement(el, 'right')">⫸</button>
                <button type="button" title="Top" (click)="alignElement(el, 'top')">⬆</button>
                <button type="button" title="Middle V" (click)="alignElement(el, 'middleV')">⬍</button>
                <button type="button" title="Bottom" (click)="alignElement(el, 'bottom')">⬇</button>
              </div>
              <div class="align-labels">
                <span>Left</span><span>Center</span><span>Right</span>
                <span>Top</span><span>Middle</span><span>Bottom</span>
              </div>
            </div>
          </details>

          <details class="acc" open>
            <summary>Location &amp; size</summary>
            <div class="acc-body grid2">
              <label>X</label><input type="number" [(ngModel)]="el.x" name="x" (ngModelChange)="onPropChanged()" />
              <label>Y</label><input type="number" [(ngModel)]="el.y" name="y" (ngModelChange)="onPropChanged()" />
              <label>W</label><input type="number" [(ngModel)]="el.w" name="w" (ngModelChange)="onPropChanged()" />
              <label>H</label><input type="number" [(ngModel)]="el.h" name="h" (ngModelChange)="onPropChanged()" />
              <label>Z-Index</label><input type="number" [(ngModel)]="el.zIndex" name="z" (ngModelChange)="onPropChanged()" />
            </div>
            <p class="hint">Drag corner/edge points on the canvas to resize.</p>
          </details>

          <details class="acc" open *ngIf="el.type==='text'">
            <summary>Typography</summary>
            <div class="acc-body">
              <label>Font family</label>
              <select [(ngModel)]="el.fontFamily" name="ff">
                <option *ngFor="let f of availableFonts()" [ngValue]="f">{{ f }}</option>
              </select>
              <label class="font-upload">
                Or add font file
                <input type="file" accept=".ttf,.otf,.woff,.woff2" (change)="onFontFile($event, el)" />
              </label>
              <label>Font size</label>
              <input type="number" [(ngModel)]="el.fontSize" name="fs" />
              <label>Text align</label>
              <select [(ngModel)]="el.align" name="align" (ngModelChange)="onPropChanged()">
                <option value="left">Left</option>
                <option value="center">Center</option>
                <option value="right">Right</option>
              </select>
              <label>Text direction</label>
              <select [(ngModel)]="el.direction" name="direction" (ngModelChange)="onPropChanged()">
                <option value="ltr">LTR (Left to right)</option>
                <option value="rtl">RTL (Right to left)</option>
              </select>
              <label class="check"><input type="checkbox" [(ngModel)]="el.autoSize" name="autoSize" (ngModelChange)="onPropChanged()" /> Auto size font to width</label>
              <label class="check"><input type="checkbox" [(ngModel)]="el.wrapText" name="wrapText" (ngModelChange)="onPropChanged()" /> Wrap text</label>
            </div>
          </details>

          <details class="acc" open>
            <summary>Appearance</summary>
            <div class="acc-body">
              <label>{{ el.type==='rect' ? 'Fill color' : 'Text / stroke color' }}</label>
              <input type="color" [(ngModel)]="el.color" name="color" />
              <label>Element background</label>
              <div class="row">
                <input type="color" [ngModel]="el.backgroundColor || '#ffffff'" (ngModelChange)="el.backgroundColor=$event" name="ebg" />
                <button type="button" class="secondary" (click)="el.backgroundColor=undefined">Clear</button>
              </div>
            </div>
          </details>

          <button type="button" class="danger" (click)="remove(el)">Delete element</button>
        </aside>
        <ng-template #noSel>
          <aside class="props">
            <h4>Properties</h4>
            <p class="hint">Select an element from the list or canvas.</p>
          </aside>
        </ng-template>
      </div>
    </div>
  `,
  styles: [`
    .designer { display: grid; gap: .75rem; }
    .toolbar { display: flex; flex-wrap: wrap; gap: .4rem; align-items: center; }
    .toolbar button, .secondary, .el-actions button, .align-grid button, .font-list button {
      padding: .45rem .7rem; border-radius: 8px; border: 1px solid #c9d4e0; background: #fff; cursor: pointer;
    }
    .toolbar button.active, .toolbar .primary { background: #1c4b73; color: #fff; border-color: #1c4b73; }
    .toolbar button:disabled { opacity: .45; cursor: not-allowed; }
    .img-el { width: 100%; height: 100%; }
    .img-el img { width: 100%; height: 100%; object-fit: contain; display: block; }
    input[type=range] { width: 100%; }
    .sep { width: 1px; height: 24px; background: #c9d4e0; margin: 0 .2rem; }
    .inline { display: inline-flex; gap: .35rem; align-items: center; font-size: .85rem; color: #456; }
    .workspace { display: grid; grid-template-columns: 220px 1fr 280px; gap: 1rem; align-items: start; }
    .left-panel, .props { background: #fff; border-radius: 12px; padding: .9rem; display: grid; gap: .55rem; align-content: start; min-height: 360px; }
    .left-panel h4, .props h4 { margin: 0; }
    .fonts-title { margin-top: .75rem; }
    .hint { margin: 0; color: #678; font-size: .85rem; }
    .el-list, .font-list { list-style: none; margin: 0; padding: 0; display: grid; gap: .35rem; }
    .el-list li, .font-list li {
      display: flex; justify-content: space-between; gap: .4rem; align-items: center;
      border: 1px solid #e1e8f0; border-radius: 8px; padding: .45rem .55rem; cursor: pointer;
    }
    .font-list li { cursor: default; font-size: .85rem; }
    .el-list li.active { border-color: #1c4b73; background: #eef5fb; }
    .el-title { display: grid; gap: .1rem; min-width: 0; }
    .el-title small { color: #678; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .el-actions { display: flex; gap: .2rem; }
    .el-actions button, .font-list button { padding: .15rem .4rem; }
    .danger-lite { color: #b00020; }
    .font-upload {
      display: grid; gap: .25rem; font-size: .8rem; color: #456;
    }
    .font-upload input[type=file] { font-size: .75rem; }
    .canvas-wrap { overflow: auto; background: #d9e2ec; border-radius: 12px; padding: 1rem; min-height: 360px; }
    .canvas { position: relative; width: 506px; height: 319px; border: 1px solid #99a; box-shadow: 0 10px 30px rgba(0,0,0,.12); overflow: visible; }
    .grid-overlay {
      pointer-events: none; position: absolute; inset: 0; z-index: 0;
      background-image:
        linear-gradient(to right, rgba(28, 75, 115, .18) 1px, transparent 1px),
        linear-gradient(to bottom, rgba(28, 75, 115, .18) 1px, transparent 1px);
      background-size: var(--grid, 10px) var(--grid, 10px);
    }
    .check input { width: auto; }
    .el {
      position: absolute; border: 1px dashed transparent; cursor: move;
      user-select: none; box-sizing: border-box; z-index: 1;
    }
    .el.selected { border-color: #1c4b73; outline: 1px solid rgba(28,75,115,.35); }
    .el-body { width: 100%; height: 100%; overflow: hidden; }
    .text-el {
      width: 100%; height: 100%; line-height: 1.2;
      letter-spacing: normal;
      word-spacing: normal;
      font-kerning: normal;
      font-variant-ligatures: common-ligatures;
      box-sizing: border-box;
    }
    .text-el.ta-left { text-align: left; }
    .text-el.ta-center { text-align: center; }
    .text-el.ta-right { text-align: right; }
    .text-el.dir-ltr { direction: ltr; unicode-bidi: isolate; }
    .text-el.dir-rtl { direction: rtl; unicode-bidi: isolate; }
    .text-el.wrap { white-space: normal; word-break: break-word; }
    .text-el:not(.wrap) { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .placeholder { width: 100%; height: 100%; display: grid; place-items: center; background: #eef2f6; font-size: .75rem; color: #567; }
    .qr-el { width: 100%; height: 100%; background: #fff; display: grid; place-items: center; }
    .qr-el img { width: 100%; height: 100%; object-fit: contain; image-rendering: pixelated; }
    .rect { width: 100%; height: 100%; }
    .handle {
      position: absolute; width: 10px; height: 10px; background: #fff;
      border: 2px solid #1c4b73; border-radius: 2px; z-index: 5; box-sizing: border-box;
    }
    .handle.nw { left: -5px; top: -5px; cursor: nwse-resize; }
    .handle.n  { left: calc(50% - 5px); top: -5px; cursor: ns-resize; }
    .handle.ne { right: -5px; top: -5px; cursor: nesw-resize; }
    .handle.e  { right: -5px; top: calc(50% - 5px); cursor: ew-resize; }
    .handle.se { right: -5px; bottom: -5px; cursor: nwse-resize; }
    .handle.s  { left: calc(50% - 5px); bottom: -5px; cursor: ns-resize; }
    .handle.sw { left: -5px; bottom: -5px; cursor: nesw-resize; }
    .handle.w  { left: -5px; top: calc(50% - 5px); cursor: ew-resize; }
    .align-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: .35rem; }
    .align-grid button { padding: .4rem; font-size: 1rem; }
    .align-labels { display: grid; grid-template-columns: repeat(3, 1fr); gap: .35rem; font-size: .7rem; color: #678; text-align: center; }
    .acc { border: 1px solid #e1e8f0; border-radius: 8px; padding: .2rem .55rem .55rem; }
    .acc summary { cursor: pointer; font-weight: 600; padding: .35rem 0; }
    .acc-body { display: grid; gap: .35rem; }
    .acc-body.grid2 { grid-template-columns: auto 1fr; align-items: center; gap: .35rem .5rem; }
    .props label { font-size: .8rem; color: #456; }
    .props input, .props select { padding: .4rem .5rem; border: 1px solid #c9d4e0; border-radius: 6px; width: 100%; box-sizing: border-box; }
    .check { display: flex; align-items: center; gap: .45rem; }
    .check input { width: auto; }
    .row { display: flex; gap: .4rem; align-items: center; }
    .danger { background: #b00020; color: #fff; border: 0; padding: .55rem; border-radius: 8px; cursor: pointer; }
    .secondary { background: #e8eef5; color: #1c4b73; }
    @media (max-width: 1100px) { .workspace { grid-template-columns: 1fr; } }
  `]
})
export class DesignerComponent implements OnChanges, OnDestroy {
  @Input() designJson = '';
  @Input() tableFields: TableFieldOption[] = [];
  @Input() assetBaseUrl = '';
  @Input() saveHandler?: (json: string) => void;
  @Input() imageUploadHandler?: (file: File) => Promise<string>;
  @ViewChild('wrap') wrap?: ElementRef<HTMLDivElement>;

  readonly cardW = 1012;
  readonly cardH = 638;

  design = signal<CardDesign>(this.empty());
  side = signal<'front' | 'back'>('front');
  selected = signal<DesignElement | null>(null);
  showGrid = signal(true);
  snapToGrid = signal(true);
  gridSize = signal(20);
  customFonts = signal<CustomFontDefinition[]>([]);
  canUndo = signal(false);
  canRedo = signal(false);
  /** Forces template refresh for mutable element props */
  private uiTick = signal(0);
  scale = 0.5;

  private mode: 'drag' | 'resize' | null = null;
  private resizeHandle: ResizeHandle | null = null;
  private activeEl: DesignElement | null = null;
  private startMouseX = 0;
  private startMouseY = 0;
  private startBox = { x: 0, y: 0, w: 0, h: 0 };
  private offsetX = 0;
  private offsetY = 0;
  private qrCache = new Map<string, string>();
  private loadedFontFaces = new Map<string, FontFace>();
  private history: string[] = [];
  private historyIndex = -1;
  private applyingHistory = false;
  private historyTimer: ReturnType<typeof setTimeout> | null = null;
  private readonly maxHistory = 60;

  ngOnChanges() {
    try {
      const parsed = JSON.parse(this.designJson || '{}');
      this.design.set({
        designVersion: parsed.designVersion || 1,
        fonts: parsed.fonts || [],
        front: parsed.front || { backgroundColor: '#FFFFFF', elements: [] },
        back: parsed.back || { backgroundColor: '#FFFFFF', elements: [] }
      });
      this.customFonts.set([...(parsed.fonts || [])]);
      void this.applyCustomFonts(this.customFonts());
      this.refreshAllQr();
    } catch {
      this.design.set(this.empty());
      this.customFonts.set([]);
    }
    if (!this.design().front.elements) this.design().front.elements = [];
    if (!this.design().back.elements) this.design().back.elements = [];
    for (const side of [this.design().front, this.design().back]) {
      for (const el of side.elements) {
        if (!el.direction) el.direction = 'ltr';
        if (!el.align) el.align = 'left';
        if (el.opacity == null && (el.type === 'image' || el.type === 'photo')) el.opacity = 1;
      }
    }
    this.resetHistory();
  }

  ngOnDestroy() {
    if (this.historyTimer) clearTimeout(this.historyTimer);
    for (const face of this.loadedFontFaces.values()) {
      try { document.fonts.delete(face); } catch { /* ignore */ }
    }
    this.loadedFontFaces.clear();
  }

  @HostListener('document:keydown', ['$event'])
  onKeyDown(ev: KeyboardEvent) {
    const target = ev.target as HTMLElement | null;
    const tag = target?.tagName?.toLowerCase();
    if (tag === 'input' || tag === 'textarea' || tag === 'select' || target?.isContentEditable) return;
    const mod = ev.ctrlKey || ev.metaKey;
    if (!mod) return;
    const key = ev.key.toLowerCase();
    if (key === 'z' && !ev.shiftKey) {
      ev.preventDefault();
      this.undo();
    } else if (key === 'y' || (key === 'z' && ev.shiftKey)) {
      ev.preventDefault();
      this.redo();
    }
  }

  bump() {
    this.uiTick.update(v => v + 1);
  }

  undo() {
    if (this.historyIndex <= 0) return;
    this.historyIndex--;
    this.applyHistoryEntry();
  }

  redo() {
    if (this.historyIndex >= this.history.length - 1) return;
    this.historyIndex++;
    this.applyHistoryEntry();
  }

  onPropChanged() {
    this.bump();
    this.scheduleHistory();
  }

  opacityPct(el: DesignElement) {
    return Math.round((el.opacity ?? 1) * 100);
  }

  isFullyTransparent(el: DesignElement) {
    return (el.opacity ?? 1) <= 0;
  }

  setOpacityPct(el: DesignElement, pct: number) {
    el.opacity = Math.max(0, Math.min(100, +pct)) / 100;
    this.onPropChanged();
  }

  setFullyTransparent(el: DesignElement, on: boolean) {
    el.opacity = on ? 0 : 1;
    this.recordHistory();
    this.bump();
  }

  resolveAssetUrl(src: string) {
    if (!src) return '';
    if (src.startsWith('data:') || src.startsWith('http://') || src.startsWith('https://') || src.startsWith('blob:'))
      return src;
    if (src.startsWith('/')) return `${this.assetBaseUrl}${src}`;
    return src;
  }

  async onImageFile(ev: Event, el: DesignElement) {
    const input = ev.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    try {
      if (this.imageUploadHandler) {
        el.src = await this.imageUploadHandler(file);
      } else {
        const dataUrl = await new Promise<string>((resolve, reject) => {
          const reader = new FileReader();
          reader.onload = () => resolve(String(reader.result || ''));
          reader.onerror = () => reject(reader.error);
          reader.readAsDataURL(file);
        });
        el.src = dataUrl;
      }
      if (el.opacity == null) el.opacity = 1;
      this.recordHistory();
      this.bump();
    } catch {
      alert('Could not upload image. Check API is running and try again.');
    } finally {
      input.value = '';
    }
  }

  currentSide() {
    this.uiTick();
    return this.side() === 'front' ? this.design().front : this.design().back;
  }

  sortedElements() {
    return [...this.currentSide().elements].sort((a, b) => (a.zIndex || 0) - (b.zIndex || 0));
  }

  availableFonts() {
    return [...BUILTIN_FONTS, ...this.customFonts().map(f => f.family)];
  }

  quotedFont(family: string) {
    if (!family) return 'Arial, sans-serif';
    return family.includes(' ') ? `"${family}", sans-serif` : `${family}, sans-serif`;
  }

  setSide(side: 'front' | 'back') {
    this.side.set(side);
    this.selected.set(null);
  }

  setCardBg(color: string) {
    this.currentSide().backgroundColor = color;
    this.recordHistory();
    this.bump();
  }

  select(el: DesignElement) {
    this.selected.set(el);
    if (el.type === 'qr') void this.ensureQr(el);
  }

  displayText(el: DesignElement) {
    return el.field ? `{{${el.field}}}` : (el.text || 'Text');
  }

  qrPreview(el: DesignElement): string | null {
    this.uiTick();
    const key = this.qrKey(el);
    return this.qrCache.get(key) || null;
  }

  onFieldPicked(el: DesignElement, fieldName: string | undefined) {
    el.field = fieldName || undefined;
    if (el.type === 'qr') void this.ensureQr(el);
    this.recordHistory();
    this.bump();
  }

  onTextChanged(el: DesignElement) {
    if (el.type === 'qr') void this.ensureQr(el);
    if (el.type === 'text' && this.containsRtl(el.text || '')) {
      if (!el.direction || el.direction === 'ltr') el.direction = 'rtl';
      if (!el.align || el.align === 'left') el.align = 'right';
    }
    this.onPropChanged();
  }

  private containsRtl(text: string) {
    return /[\u0590-\u08FF\uFB50-\uFDFF\uFE70-\uFEFF]/.test(text);
  }

  alignElement(el: DesignElement, mode: 'left' | 'centerH' | 'right' | 'top' | 'middleV' | 'bottom') {
    switch (mode) {
      case 'left': el.x = 0; break;
      case 'centerH': el.x = Math.max(0, (this.cardW - el.w) / 2); break;
      case 'right': el.x = Math.max(0, this.cardW - el.w); break;
      case 'top': el.y = 0; break;
      case 'middleV': el.y = Math.max(0, (this.cardH - el.h) / 2); break;
      case 'bottom': el.y = Math.max(0, this.cardH - el.h); break;
    }
    if (this.snapToGrid() && this.showGrid()) {
      el.x = this.snap(el.x);
      el.y = this.snap(el.y);
    }
    this.recordHistory();
    this.bump();
  }

  add(type: string) {
    const defaultField =
      type === 'photo' ? this.preferField(['photo', 'photo_binary']) :
      type === 'qr' ? this.preferField(['string', 'number']) :
      type === 'text' ? this.preferField(['string', 'number', 'date']) :
      undefined;

    const el: DesignElement = {
      id: crypto.randomUUID(),
      type,
      field: defaultField,
      text: type === 'text' && !defaultField ? 'Sample' : (type === 'qr' && !defaultField ? 'https://example.com' : undefined),
      x: 40, y: 40,
      w: type === 'photo' || type === 'qr' ? 120 : 160,
      h: type === 'photo' || type === 'qr' ? 120 : 36,
      fontSize: 18,
      fontFamily: 'Arial',
      color: type === 'rect' ? '#1c4b73' : '#111111',
      align: 'left',
      direction: 'ltr',
      zIndex: this.currentSide().elements.length + 1,
      autoSize: false,
      wrapText: false,
      backgroundColor: undefined,
      opacity: type === 'image' || type === 'photo' ? 1 : undefined
    };
    this.currentSide().elements.push(el);
    this.selected.set(el);
    if (type === 'qr') void this.ensureQr(el);
    this.recordHistory();
    this.bump();
  }

  preferField(types: string[]) {
    const match = this.tableFields.find(f => types.includes((f.dataType || 'string').toLowerCase()));
    return match?.name || this.tableFields[0]?.name;
  }

  remove(el: DesignElement) {
    const side = this.currentSide();
    side.elements = side.elements.filter(e => e.id !== el.id);
    if (this.selected()?.id === el.id) this.selected.set(null);
    this.recordHistory();
    this.bump();
  }

  moveZ(el: DesignElement, delta: number) {
    el.zIndex = (el.zIndex || 0) + delta;
    this.recordHistory();
    this.bump();
  }

  async onFontFile(ev: Event, applyTo?: DesignElement) {
    const input = ev.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    try {
      const buffer = await file.arrayBuffer();
      const bytes = new Uint8Array(buffer);
      let binary = '';
      for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
      const dataBase64 = btoa(binary);
      const ext = (file.name.split('.').pop() || 'ttf').toLowerCase();
      const format = ext === 'otf' ? 'opentype' : ext === 'woff' || ext === 'woff2' ? ext : 'truetype';
      const family = file.name.replace(/\.[^.]+$/, '').replace(/[_-]+/g, ' ').trim() || 'CustomFont';

      const def: CustomFontDefinition = { family, dataBase64, format };
      const next = [...this.customFonts().filter(f => f.family !== family), def];
      this.customFonts.set(next);
      this.design().fonts = next;
      await this.applyCustomFonts([def]);
      if (applyTo) applyTo.fontFamily = family;
      this.recordHistory();
      this.bump();
    } catch {
      alert('Could not load font file.');
    } finally {
      input.value = '';
    }
  }

  removeFont(family: string) {
    const next = this.customFonts().filter(f => f.family !== family);
    this.customFonts.set(next);
    this.design().fonts = next;
    const face = this.loadedFontFaces.get(family);
    if (face) {
      try { document.fonts.delete(face); } catch { /* ignore */ }
      this.loadedFontFaces.delete(family);
    }
    for (const side of [this.design().front, this.design().back]) {
      for (const el of side.elements) {
        if (el.fontFamily === family) el.fontFamily = 'Arial';
      }
    }
    this.recordHistory();
    this.bump();
  }

  startDrag(ev: MouseEvent, el: DesignElement) {
    if ((ev.target as HTMLElement).classList.contains('handle')) return;
    this.mode = 'drag';
    this.activeEl = el;
    this.offsetX = ev.offsetX;
    this.offsetY = ev.offsetY;
    this.selected.set(el);
    ev.preventDefault();
    ev.stopPropagation();
  }

  startResize(ev: MouseEvent, el: DesignElement, handle: ResizeHandle) {
    this.mode = 'resize';
    this.resizeHandle = handle;
    this.activeEl = el;
    this.startMouseX = ev.clientX;
    this.startMouseY = ev.clientY;
    this.startBox = { x: el.x, y: el.y, w: el.w, h: el.h };
    this.selected.set(el);
    ev.preventDefault();
    ev.stopPropagation();
  }

  onPointerMove(ev: MouseEvent) {
    if (!this.activeEl || !this.mode || !this.wrap) return;
    const canvas = this.wrap.nativeElement.querySelector('.canvas') as HTMLElement;
    const rect = canvas.getBoundingClientRect();

    if (this.mode === 'drag') {
      let x = Math.max(0, (ev.clientX - rect.left - this.offsetX) / this.scale);
      let y = Math.max(0, (ev.clientY - rect.top - this.offsetY) / this.scale);
      if (this.snapToGrid() && this.showGrid()) {
        x = this.snap(x);
        y = this.snap(y);
      }
      this.activeEl.x = Math.min(x, this.cardW - this.activeEl.w);
      this.activeEl.y = Math.min(y, this.cardH - this.activeEl.h);
      this.bump();
      return;
    }

    if (this.mode === 'resize' && this.resizeHandle) {
      const dx = (ev.clientX - this.startMouseX) / this.scale;
      const dy = (ev.clientY - this.startMouseY) / this.scale;
      let { x, y, w, h } = this.startBox;
      const minSize = 10;
      const handle = this.resizeHandle;

      if (handle.includes('e')) w = Math.max(minSize, this.startBox.w + dx);
      if (handle.includes('s')) h = Math.max(minSize, this.startBox.h + dy);
      if (handle.includes('w')) {
        w = Math.max(minSize, this.startBox.w - dx);
        x = this.startBox.x + (this.startBox.w - w);
      }
      if (handle.includes('n')) {
        h = Math.max(minSize, this.startBox.h - dy);
        y = this.startBox.y + (this.startBox.h - h);
      }

      if (this.snapToGrid() && this.showGrid()) {
        x = this.snap(x);
        y = this.snap(y);
        w = Math.max(minSize, this.snap(w));
        h = Math.max(minSize, this.snap(h));
      }

      this.activeEl.x = Math.max(0, Math.min(x, this.cardW - minSize));
      this.activeEl.y = Math.max(0, Math.min(y, this.cardH - minSize));
      this.activeEl.w = Math.min(w, this.cardW - this.activeEl.x);
      this.activeEl.h = Math.min(h, this.cardH - this.activeEl.y);
      this.bump();
    }
  }

  endInteraction() {
    const ended = !!this.mode;
    this.mode = null;
    this.resizeHandle = null;
    this.activeEl = null;
    if (ended) this.recordHistory();
  }

  emitSave() {
    const d = this.design();
    d.fonts = this.customFonts();
    this.saveHandler?.(JSON.stringify(d));
  }

  private qrKey(el: DesignElement) {
    return `${el.id}|${el.field || ''}|${el.text || ''}`;
  }

  private qrPayload(el: DesignElement) {
    if (el.field) return `{{${el.field}}}`;
    return el.text || 'QR';
  }

  private async ensureQr(el: DesignElement) {
    const key = this.qrKey(el);
    if (this.qrCache.has(key)) {
      this.bump();
      return;
    }
    try {
      const dataUrl = await QRCode.toDataURL(this.qrPayload(el), {
        margin: 1,
        width: 256,
        errorCorrectionLevel: 'M',
        color: { dark: '#000000', light: '#ffffff' }
      });
      this.qrCache.set(key, dataUrl);
      this.bump();
    } catch {
      this.qrCache.delete(key);
    }
  }

  private refreshAllQr() {
    this.qrCache.clear();
    for (const side of [this.design().front, this.design().back]) {
      for (const el of side.elements) {
        if (el.type === 'qr') void this.ensureQr(el);
      }
    }
  }

  private async applyCustomFonts(fonts: CustomFontDefinition[]) {
    for (const f of fonts) {
      if (this.loadedFontFaces.has(f.family)) continue;
      try {
        const mime =
          f.format === 'opentype' ? 'font/otf' :
          f.format === 'woff' ? 'font/woff' :
          f.format === 'woff2' ? 'font/woff2' :
          'font/ttf';
        const raw = f.dataBase64.includes(',') ? f.dataBase64.split(',')[1] : f.dataBase64;
        const url = `data:${mime};base64,${raw}`;
        const face = new FontFace(f.family, `url(${url})`, { style: 'normal', weight: '400' });
        await face.load();
        document.fonts.add(face);
        this.loadedFontFaces.set(f.family, face);
      } catch {
        // ignore bad fonts
      }
    }
  }

  private snap(v: number) {
    const g = this.gridSize();
    return Math.round(v / g) * g;
  }

  private resetHistory() {
    this.history = [JSON.stringify(this.design())];
    this.historyIndex = 0;
    this.updateUndoFlags();
  }

  private scheduleHistory() {
    if (this.applyingHistory) return;
    if (this.historyTimer) clearTimeout(this.historyTimer);
    this.historyTimer = setTimeout(() => this.recordHistory(), 350);
  }

  private recordHistory() {
    if (this.applyingHistory) return;
    if (this.historyTimer) {
      clearTimeout(this.historyTimer);
      this.historyTimer = null;
    }
    const json = JSON.stringify(this.design());
    if (json === this.history[this.historyIndex]) return;
    this.history = this.history.slice(0, this.historyIndex + 1);
    this.history.push(json);
    if (this.history.length > this.maxHistory) {
      this.history.shift();
    }
    this.historyIndex = this.history.length - 1;
    this.updateUndoFlags();
  }

  private applyHistoryEntry() {
    const json = this.history[this.historyIndex];
    if (!json) return;
    this.applyingHistory = true;
    try {
      const parsed = JSON.parse(json) as CardDesign;
      this.design.set({
        designVersion: parsed.designVersion || 1,
        fonts: parsed.fonts || [],
        front: parsed.front || { backgroundColor: '#FFFFFF', elements: [] },
        back: parsed.back || { backgroundColor: '#FFFFFF', elements: [] }
      });
      this.customFonts.set([...(parsed.fonts || [])]);
      void this.applyCustomFonts(this.customFonts());
      this.selected.set(null);
      this.refreshAllQr();
      this.bump();
    } finally {
      this.applyingHistory = false;
      this.updateUndoFlags();
    }
  }

  private updateUndoFlags() {
    this.canUndo.set(this.historyIndex > 0);
    this.canRedo.set(this.historyIndex >= 0 && this.historyIndex < this.history.length - 1);
  }

  private empty(): CardDesign {
    return {
      designVersion: 1,
      fonts: [],
      front: { backgroundColor: '#FFFFFF', elements: [] },
      back: { backgroundColor: '#FFFFFF', elements: [] }
    };
  }
}
