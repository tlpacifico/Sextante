import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { SelectModule } from 'primeng/select';
import { CheckboxModule } from 'primeng/checkbox';
import { InputTextModule } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { StepsModule } from 'primeng/steps';
import { MenuItem } from 'primeng/api';
import { FinancialApiService } from '../../../core/api/financial-api.service';
import {
  UploadCsvResponse,
  ImportProfileDto,
  PreviewRow,
  ColumnMappingInput,
  ConfirmImportResponse,
  TRANSACTION_FIELD_LABELS,
} from '../../../core/api/financial.types';

type WizardStep = 1 | 2 | 3 | 4;

const DELIMITER_OPTIONS = [
  { value: ',', label: 'Vírgula (,)' },
  { value: ';', label: 'Ponto e vírgula (;)' },
  { value: '\t', label: 'Tab' },
  { value: '|', label: 'Pipe (|)' },
];

const DATE_FORMAT_OPTIONS = [
  { value: 'yyyy-MM-dd', label: 'AAAA-MM-DD' },
  { value: 'dd/MM/yyyy', label: 'DD/MM/AAAA' },
  { value: 'MM/dd/yyyy', label: 'MM/DD/AAAA' },
  { value: 'dd-MM-yyyy', label: 'DD-MM-AAAA' },
];

const DECIMAL_SEPARATOR_OPTIONS = [
  { value: '.', label: 'Ponto (1,000.50)' },
  { value: ',', label: 'Vírgula (1.000,50)' },
];

@Component({
  selector: 'app-import-wizard-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    RouterLink,
    ButtonModule,
    SelectModule,
    CheckboxModule,
    InputTextModule,
    TableModule,
    TagModule,
    ToastModule,
    StepsModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [MessageService],
  template: `
    <div class="max-w-5xl mx-auto">
      <h1 class="text-xl md:text-2xl font-semibold mb-6">Nova importação</h1>

      <div class="mb-8">
        <p-steps [model]="steps()" [activeIndex]="activeStep() - 1" [readonly]="true"></p-steps>
      </div>

      <!-- Step 1 – Upload -->
      @if (activeStep() === 1) {
        <div class="max-w-lg">
          <h2 class="text-lg font-medium mb-4">1. Selecionar ficheiro</h2>

          <div class="flex flex-col gap-4">
            <div class="flex flex-col gap-1">
              <label for="wiz-profile">Perfil de importação (opcional)</label>
              <p-select
                inputId="wiz-profile"
                [options]="profiles()"
                optionLabel="name"
                optionValue="id"
                [formControl]="uploadForm.controls.importProfileId"
                styleClass="w-full"
                placeholder="Nenhum"
                [showClear]="true"
              ></p-select>
            </div>

            <div
              class="border-2 border-dashed border-[var(--p-surface-300)] dark:border-[var(--p-surface-600)]
                     rounded-lg p-8 text-center hover:border-[var(--p-primary-color)] transition-colors
                     cursor-pointer"
              [class.border-[var(--p-primary-color)]]="dragOver()"
              (dragover)="onDragOver($event)"
              (dragleave)="onDragLeave($event)"
              (drop)="onDrop($event)"
              (click)="fileInput.click()"
            >
              <input #fileInput type="file" accept=".csv" class="hidden" (change)="onFileSelected($event)" />
              @if (!selectedFile()) {
                <div class="flex flex-col items-center gap-3">
                  <i class="pi pi-file-import text-3xl text-[var(--p-text-muted-color)]"></i>
                  <span class="text-[var(--p-text-muted-color)]">
                    Arraste um ficheiro CSV ou clique para selecionar
                  </span>
                  <span class="text-xs text-[var(--p-text-muted-color)]">
                    Formatos aceites: CSV (delimitado por vírgula, ponto-e-vírgula, tab ou pipe)
                  </span>
                </div>
              } @else {
                <div class="flex flex-col items-center gap-2">
                  <i class="pi pi-file text-3xl text-[var(--p-primary-color)]"></i>
                  <span class="font-medium">{{ selectedFile()?.name }}</span>
                  <span class="text-sm text-[var(--p-text-muted-color)]">
                    {{ (selectedFile()!.size / 1024).toFixed(0) }} KB
                  </span>
                  <p-button
                    label="Remover"
                    icon="pi pi-times"
                    severity="secondary"
                    [text]="true"
                    size="small"
                    type="button"
                    (onClick)="clearFile(); $event.stopPropagation()"
                  ></p-button>
                </div>
              }
            </div>

            <div class="flex justify-end pt-2">
              <p-button
                label="Analisar ficheiro"
                icon="pi pi-arrow-right"
                [disabled]="!selectedFile() || uploading()"
                [loading]="uploading()"
                (onClick)="analyzeFile()"
              ></p-button>
            </div>
          </div>

          @if (uploadResponse()?.errors?.length) {
            <div class="mt-4 p-3 rounded border border-red-300 dark:border-red-800 bg-red-50 dark:bg-red-950">
              @for (err of uploadResponse()?.errors; track err) {
                <div class="text-sm text-red-600 dark:text-red-400 flex items-center gap-2">
                  <i class="pi pi-exclamation-triangle"></i>
                  {{ err }}
                </div>
              }
            </div>
          }
        </div>
      }

      <!-- Step 2 – Column Mapping -->
      @if (activeStep() === 2 && uploadResponse(); as response) {
        <div>
          <h2 class="text-lg font-medium mb-4">2. Mapeamento de colunas</h2>

          <div class="flex flex-wrap items-center gap-4 mb-4">
            <div class="flex flex-col gap-1">
              <label for="wiz-delim">Delimitador</label>
              <p-select
                inputId="wiz-delim"
                [options]="delimiterOptions"
                optionLabel="label"
                optionValue="value"
                [formControl]="mappingForm.controls.delimiter"
                styleClass="w-full"
              ></p-select>
            </div>
            <div class="flex items-center gap-2 pt-6">
              <p-checkbox
                inputId="wiz-hasheader"
                [binary]="true"
                [formControl]="mappingForm.controls.hasHeaderRow"
              ></p-checkbox>
              <label for="wiz-hasheader">Cabeçalho na primeira linha</label>
            </div>
            <div class="flex flex-col gap-1">
              <label for="wiz-datefmt">Formato data</label>
              <p-select
                inputId="wiz-datefmt"
                [options]="dateFormatOptions"
                optionLabel="label"
                optionValue="value"
                [formControl]="mappingForm.controls.dateFormat"
                styleClass="w-full"
              ></p-select>
            </div>
            <div class="flex flex-col gap-1">
              <label for="wiz-dec">Decimal</label>
              <p-select
                inputId="wiz-dec"
                [options]="decimalSeparatorOptions"
                optionLabel="label"
                optionValue="value"
                [formControl]="mappingForm.controls.decimalSeparator"
                styleClass="w-full"
              ></p-select>
            </div>
          </div>

          <div class="overflow-x-auto border border-[var(--p-surface-300)] dark:border-[var(--p-surface-700)] rounded mb-4">
            <table class="w-full text-sm">
              <thead class="bg-[var(--p-surface-100)] dark:bg-[var(--p-surface-800)]">
                <tr>
                  @for (header of response.headers; track header) {
                    <th class="px-3 py-2 text-left font-medium">{{ header }}</th>
                  }
                </tr>
                <tr>
                  @for (header of response.headers; track header; let i = $index) {
                    <th class="px-3 py-2">
                      <p-select
                        [options]="fieldOptions()"
                        optionLabel="label"
                        optionValue="value"
                        [formControl]="columnForm(header)"
                        styleClass="w-full"
                        [showClear]="true"
                        placeholder="Ignorar"
                      ></p-select>
                    </th>
                  }
                </tr>
              </thead>
              <tbody>
                @for (row of response.previewRows.slice(0, 5); track row.rowIndex) {
                  <tr class="border-t border-[var(--p-surface-200)] dark:border-[var(--p-surface-700)]">
                    @for (value of row.values; track $index) {
                      <td class="px-3 py-2">{{ value }}</td>
                    }
                  </tr>
                }
              </tbody>
            </table>
          </div>

          @if (response.truncated) {
            <div class="text-sm text-[var(--p-text-muted-color)] mb-4">
              <i class="pi pi-info-circle mr-1"></i>
              Pré-visualização limitada a 5 linhas. {{ response.totalRowCount }} linhas totais no ficheiro.
            </div>
          }

          <div class="flex justify-between pt-2">
            <p-button
              label="Voltar"
              icon="pi pi-arrow-left"
              severity="secondary"
              [text]="true"
              type="button"
              (onClick)="goToStep(1)"
            ></p-button>
            <p-button
              label="Atualizar pré-visualização"
              icon="pi pi-refresh"
              [disabled]="updating()"
              [loading]="updating()"
              (onClick)="updatePreview()"
            ></p-button>
          </div>
        </div>
      }

      <!-- Step 3 – Preview + Confirm -->
      @if (activeStep() === 3 && uploadResponse(); as response) {
        <div>
          <h2 class="text-lg font-medium mb-4">3. Confirmar importação</h2>

          <div class="flex flex-wrap gap-4 mb-4 text-sm">
            <div class="px-3 py-2 rounded bg-[var(--p-surface-100)] dark:bg-[var(--p-surface-800)]">
              <span class="text-[var(--p-text-muted-color)]">Total: </span>
              <span class="font-medium">{{ response.totalRowCount }}</span>
            </div>
            <div class="px-3 py-2 rounded bg-[var(--p-surface-100)] dark:bg-[var(--p-surface-800)]">
              <span class="text-[var(--p-text-muted-color)]">Duplicados: </span>
              <span class="font-medium text-orange-500">{{ duplicateCount() }}</span>
            </div>
            <div class="px-3 py-2 rounded bg-[var(--p-surface-100)] dark:bg-[var(--p-surface-800)]">
              <span class="text-[var(--p-text-muted-color)]">A categorizar: </span>
              <span class="font-medium text-green-500">{{ autoCategorizedCount() }}</span>
            </div>
            <div class="px-3 py-2 rounded bg-[var(--p-surface-100)] dark:bg-[var(--p-surface-800)]">
              <span class="text-[var(--p-text-muted-color)]">Com erros: </span>
              <span class="font-medium text-red-500">{{ errorCount() }}</span>
            </div>
          </div>

          <div class="overflow-x-auto border border-[var(--p-surface-300)] dark:border-[var(--p-surface-700)] rounded">
            <table class="w-full text-sm">
              <thead class="bg-[var(--p-surface-100)] dark:bg-[var(--p-surface-800)] text-left">
                <tr>
                  <th class="px-3 py-2" style="width:2.5rem">
                    <p-checkbox
                      [binary]="true"
                      [ngModel]="allDuplicatesSelected()"
                      (onChange)="toggleAllDuplicates()"
                    ></p-checkbox>
                  </th>
                  <th class="px-3 py-2" style="width:3rem">#</th>
                  <th class="px-3 py-2">Colunas</th>
                  <th class="px-3 py-2">Categoria</th>
                  <th class="px-3 py-2" style="width:6rem">Estado</th>
                </tr>
              </thead>
              <tbody>
                @for (row of response.previewRows; track row.rowIndex) {
                  <tr class="border-t border-[var(--p-surface-200)] dark:border-[var(--p-surface-700)]"
                    [class.bg-orange-50]="row.isDuplicate"
                    [class.dark:bg-orange-950]="row.isDuplicate"
                    [class.bg-red-50]="row.error"
                    [class.dark:bg-red-950]="row.error"
                  >
                    <td class="px-3 py-2">
                      @if (row.isDuplicate) {
                        <p-checkbox
                          [binary]="true"
                          [ngModel]="duplicateSelection()[row.rowIndex]"
                          (onChange)="toggleDuplicate(row.rowIndex)"
                        ></p-checkbox>
                      }
                    </td>
                    <td class="px-3 py-2 text-xs text-[var(--p-text-muted-color)]">{{ row.rowIndex + 1 }}</td>
                    <td class="px-3 py-2 font-mono text-xs max-w-xs truncate">
                      {{ row.values.join(' | ') }}
                    </td>
                    <td class="px-3 py-2">
                      @if (row.isAutoCategorized && row.suggestedCategoryName) {
                        <p-tag [value]="row.suggestedCategoryName" [rounded]="true" severity="success"></p-tag>
                      }
                    </td>
                    <td class="px-3 py-2">
                      @if (row.error) {
                        <p-tag [value]="row.error" [rounded]="true" severity="danger"></p-tag>
                      } @else if (row.isDuplicate) {
                        <p-tag value="Duplicado" [rounded]="true" severity="warn"></p-tag>
                      } @else if (row.isAutoCategorized) {
                        <p-tag value="OK + Cat." [rounded]="true" severity="success"></p-tag>
                      } @else {
                        <p-tag value="OK" [rounded]="true" severity="info"></p-tag>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>

          <div class="flex justify-between pt-4">
            <p-button
              label="Voltar"
              icon="pi pi-arrow-left"
              severity="secondary"
              [text]="true"
              type="button"
              (onClick)="goToStep(2)"
            ></p-button>
            <p-button
              label="Confirmar importação"
              icon="pi pi-check"
              [disabled]="confirming()"
              [loading]="confirming()"
              (onClick)="confirmImport()"
            ></p-button>
          </div>
        </div>
      }

      <!-- Step 4 – Result -->
      @if (activeStep() === 4 && confirmResult(); as result) {
        <div class="max-w-lg mx-auto text-center">
          <div class="mb-4">
            <i class="pi pi-check-circle text-5xl text-green-500"></i>
          </div>
          <h2 class="text-lg font-medium mb-2">Importação concluída</h2>
          <div class="flex flex-col gap-2 mb-6">
            <div class="flex justify-between px-4 py-2 rounded bg-[var(--p-surface-100)] dark:bg-[var(--p-surface-800)]">
              <span>Linhas importadas</span>
              <span class="font-medium">{{ result.importedRows }}</span>
            </div>
            <div class="flex justify-between px-4 py-2 rounded bg-[var(--p-surface-100)] dark:bg-[var(--p-surface-800)]">
              <span>Categorizadas automaticamente</span>
              <span class="font-medium text-green-500">{{ result.autoCategorized }}</span>
            </div>
            <div class="flex justify-between px-4 py-2 rounded bg-[var(--p-surface-100)] dark:bg-[var(--p-surface-800)]">
              <span>Manuais (precisam de categoria)</span>
              <span class="font-medium text-orange-500">{{ result.manualCount }}</span>
            </div>
            <div class="flex justify-between px-4 py-2 rounded bg-[var(--p-surface-100)] dark:bg-[var(--p-surface-800)]">
              <span>Com erros</span>
              <span class="font-medium text-red-500">{{ result.errorRows }}</span>
            </div>
            <div class="flex justify-between px-4 py-2 rounded bg-[var(--p-surface-100)] dark:bg-[var(--p-surface-800)]">
              <span>Estado</span>
              <p-tag [value]="result.status === 'Completed' ? 'Concluída' : result.status" severity="success" [rounded]="true"></p-tag>
            </div>
          </div>

          <div class="flex justify-center gap-2">
            <a routerLink="/app/imports" class="no-underline">
              <p-button label="Ver importações" icon="pi pi-list" severity="secondary" type="button"></p-button>
            </a>
            <p-button
              label="Nova importação"
              icon="pi pi-plus"
              type="button"
              (onClick)="reset()"
            ></p-button>
          </div>
        </div>
      }

      <p-toast></p-toast>
    </div>
  `,
})
export class ImportWizardPage implements OnInit {
  private readonly api = inject(FinancialApiService);
  private readonly fb = inject(FormBuilder);
  private readonly toast = inject(MessageService);

  protected readonly profiles = signal<ImportProfileDto[]>([]);
  protected readonly selectedFile = signal<File | null>(null);
  protected readonly dragOver = signal(false);
  protected readonly uploading = signal(false);

  protected readonly uploadResponse = signal<UploadCsvResponse | null>(null);
  protected readonly activeStep = signal<WizardStep>(1);

  protected readonly updating = signal(false);
  protected readonly confirming = signal(false);
  protected readonly confirmResult = signal<ConfirmImportResponse | null>(null);

  protected readonly duplicateSelection = signal<Record<number, boolean>>({});

  protected readonly duplicateCount = () => {
    const r = this.uploadResponse();
    return r ? r.previewRows.filter(p => p.isDuplicate).length : 0;
  };

  protected readonly autoCategorizedCount = () => {
    const r = this.uploadResponse();
    return r ? r.previewRows.filter(p => p.isAutoCategorized).length : 0;
  };

  protected readonly errorCount = () => {
    const r = this.uploadResponse();
    return r ? r.previewRows.filter(p => p.error).length : 0;
  };

  protected readonly allDuplicatesSelected = () => {
    const r = this.uploadResponse();
    if (!r) return false;
    const dups = r.previewRows.filter(p => p.isDuplicate);
    if (dups.length === 0) return false;
    return dups.every(p => this.duplicateSelection()[p.rowIndex] ?? false);
  };

  protected readonly delimiterOptions = DELIMITER_OPTIONS;
  protected readonly dateFormatOptions = DATE_FORMAT_OPTIONS;
  protected readonly decimalSeparatorOptions = DECIMAL_SEPARATOR_OPTIONS;

  protected readonly fieldOptions = () => {
    const entries = Object.entries(TRANSACTION_FIELD_LABELS).map(([value, label]) => ({ value, label }));
    entries.unshift({ value: '', label: 'Ignorar' });
    return entries;
  };

  protected readonly steps = (): MenuItem[] => [
    { label: 'Ficheiro' },
    { label: 'Mapeamento' },
    { label: 'Confirmação' },
    { label: 'Resultado' },
  ];

  protected readonly uploadForm = this.fb.nonNullable.group({
    importProfileId: [''],
  });

  protected readonly mappingForm = this.fb.nonNullable.group({
    delimiter: [','],
    hasHeaderRow: [true],
    dateFormat: ['yyyy-MM-dd'],
    decimalSeparator: ['.'],
  });

  public columnMappingGroup = this.fb.nonNullable.group({});

  async ngOnInit(): Promise<void> {
    try {
      this.profiles.set(await this.api.listImportProfiles());
    } catch {
      // non-critical – user can import without a profile
    }
  }

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragOver.set(true);
  }

  protected onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.dragOver.set(false);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragOver.set(false);
    const files = event.dataTransfer?.files;
    if (files?.length) {
      this.setFile(files[0]);
    }
  }

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files?.length) {
      this.setFile(input.files[0]);
    }
  }

  private setFile(file: File): void {
    if (!file.name.endsWith('.csv')) {
      this.toast.add({ severity: 'warn', summary: 'Formato', detail: 'Selecione um ficheiro CSV.' });
      return;
    }
    this.selectedFile.set(file);
  }

  protected clearFile(): void {
    this.selectedFile.set(null);
    this.uploadResponse.set(null);
    this.activeStep.set(1);
  }

  protected async analyzeFile(): Promise<void> {
    const file = this.selectedFile();
    if (!file) return;

    this.uploading.set(true);
    try {
      const profileId = this.uploadForm.controls.importProfileId.value || null;
      const response = await this.api.uploadCsv(file, profileId || null);
      this.uploadResponse.set(response);

      this.mappingForm.patchValue({
        delimiter: response.detectedDelimiter,
        hasHeaderRow: response.detectedHasHeader,
      });

      this.buildColumnFormControls(response);

      if (profileId) {
        await this.applyProfileMapping(profileId);
      }

      this.activeStep.set(2);
    } catch {
      this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Erro ao analisar o ficheiro.' });
    } finally {
      this.uploading.set(false);
    }
  }

  private buildColumnFormControls(response: UploadCsvResponse): void {
    const group = this.fb.nonNullable.group({});
    for (const header of response.headers) {
      group.addControl(header, this.fb.nonNullable.control<string>(''));
    }
    (this as any).columnMappingGroup = group;
  }

  private async applyProfileMapping(profileId: string): Promise<void> {
    const profile = this.profiles().find(p => p.id === profileId);
    if (!profile) return;

    this.mappingForm.patchValue({
      delimiter: profile.delimiter,
      hasHeaderRow: profile.hasHeaderRow,
      dateFormat: profile.dateFormat,
      decimalSeparator: profile.decimalSeparator,
    });

    for (const mapping of profile.columnMappings) {
      const ctrl = this.columnMappingGroup.get(mapping.csvColumnName);
      if (ctrl) {
        ctrl.setValue(mapping.transactionField);
      }
    }

    await this.updatePreview();
  }

  protected columnForm(header: string): ReturnType<typeof this.fb.nonNullable.control> {
    return this.columnMappingGroup.get(header) as ReturnType<typeof this.fb.nonNullable.control>
      ?? this.fb.nonNullable.control('');
  }

  protected async updatePreview(): Promise<void> {
    const response = this.uploadResponse();
    if (!response) return;

    this.updating.set(true);
    try {
      const maggings: ColumnMappingInput[] = Object.keys(this.columnMappingGroup.controls).map(header => ({
        csvColumnName: header,
        transactionField: this.columnMappingGroup.get(header)?.value || null,
      }));

      const updated = await this.api.updatePreview(response.batchId, {
        columnMappings: maggings,
        delimiter: this.mappingForm.controls.delimiter.value,
        hasHeaderRow: this.mappingForm.controls.hasHeaderRow.value,
        dateFormat: this.mappingForm.controls.dateFormat.value,
        decimalSeparator: this.mappingForm.controls.decimalSeparator.value,
        skipRows: 0,
      });

      this.uploadResponse.set(updated);
      this.buildColumnFormControls(updated);
      this.duplicateSelection.set({});
      this.activeStep.set(3);
    } catch {
      this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Erro ao atualizar pré-visualização.' });
    } finally {
      this.updating.set(false);
    }
  }

  protected toggleDuplicate(rowIndex: number): void {
    const current = this.duplicateSelection();
    const updated = { ...current };
    updated[rowIndex] = !updated[rowIndex];
    this.duplicateSelection.set(updated);
  }

  protected toggleAllDuplicates(): void {
    const r = this.uploadResponse();
    if (!r) return;
    const dups = r.previewRows.filter(p => p.isDuplicate);
    const allSelected = dups.every(p => this.duplicateSelection()[p.rowIndex] ?? false);
    const updated: Record<number, boolean> = {};
    for (const p of dups) {
      updated[p.rowIndex] = !allSelected;
    }
    this.duplicateSelection.set(updated);
  }

  protected async confirmImport(): Promise<void> {
    const response = this.uploadResponse();
    if (!response) return;

    const selectedDuplicates = Object.entries(this.duplicateSelection())
      .filter(([, selected]) => selected)
      .map(([idx]) => response.previewRows[Number(idx)]?.duplicateTransactionId)
      .filter((id): id is string => !!id);

    this.confirming.set(true);
    try {
      const result = await this.api.confirmImport(response.batchId, selectedDuplicates);
      this.confirmResult.set(result);
      this.activeStep.set(4);
    } catch {
      this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Erro ao confirmar importação.' });
    } finally {
      this.confirming.set(false);
    }
  }

  protected goToStep(step: WizardStep): void {
    this.activeStep.set(step);
  }

  protected reset(): void {
    this.selectedFile.set(null);
    this.uploadResponse.set(null);
    this.confirmResult.set(null);
    this.duplicateSelection.set({});
    this.activeStep.set(1);
  }
}
