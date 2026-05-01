import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormArray, FormBuilder, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { DialogModule } from 'primeng/dialog';
import { SelectModule } from 'primeng/select';
import { InputTextModule } from 'primeng/inputtext';
import { InputNumberModule } from 'primeng/inputnumber';
import { CheckboxModule } from 'primeng/checkbox';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToastModule } from 'primeng/toast';
import { ConfirmationService, MessageService } from 'primeng/api';
import { FinancialApiService } from '../../../core/api/financial-api.service';
import {
  ImportProfileDto,
  ColumnMappingDto,
  TransactionField,
  TRANSACTION_FIELD_LABELS,
} from '../../../core/api/financial.types';

const DELIMITER_OPTIONS = [
  { value: ',', label: 'Vírgula (,)' },
  { value: ';', label: 'Ponto e vírgula (;)' },
  { value: '\t', label: 'Tab' },
  { value: '|', label: 'Pipe (|)' },
];

const DATE_FORMAT_OPTIONS = [
  { value: 'yyyy-MM-dd', label: 'AAAA-MM-DD (2024-01-31)' },
  { value: 'dd/MM/yyyy', label: 'DD/MM/AAAA (31/01/2024)' },
  { value: 'MM/dd/yyyy', label: 'MM/DD/AAAA (01/31/2024)' },
  { value: 'dd-MM-yyyy', label: 'DD-MM-AAAA (31-01-2024)' },
];

const DECIMAL_SEPARATOR_OPTIONS = [
  { value: '.', label: 'Ponto (1,000.50)' },
  { value: ',', label: 'Vírgula (1.000,50)' },
];

@Component({
  selector: 'app-import-profiles-page',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    ButtonModule,
    DialogModule,
    SelectModule,
    InputTextModule,
    InputNumberModule,
    CheckboxModule,
    TableModule,
    TagModule,
    ToastModule,
    ConfirmDialogModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [MessageService, ConfirmationService],
  template: `
    <div class="max-w-5xl mx-auto">
      <div class="flex items-center justify-between flex-wrap gap-2 mb-4">
        <h1 class="text-xl md:text-2xl font-semibold">Perfis de importação</h1>
        <p-button
          label="Novo perfil"
          icon="pi pi-plus"
          (onClick)="openCreate()"
        ></p-button>
      </div>

      <div class="overflow-x-auto">
      <p-table
        [value]="profiles()"
        [tableStyle]="{ 'min-width': '40rem' }"
        styleClass="p-datatable-sm"
        dataKey="id"
      >
        <ng-template pTemplate="header">
          <tr>
            <th>Nome</th>
            <th style="width: 7rem">Delimitador</th>
            <th style="width: 6rem">Cabeçalho</th>
            <th style="width: 10rem">Atualizado</th>
            <th style="width: 9rem"></th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-profile>
          <tr>
            <td>
              <span class="font-medium">{{ profile.name }}</span>
              <span class="text-xs text-[var(--p-text-muted-color)] ml-2">
                ({{ profile.columnMappings.length }} colunas)
              </span>
            </td>
            <td class="font-mono">{{ profile.delimiter === '\t' ? 'Tab' : profile.delimiter }}</td>
            <td>
              @if (profile.hasHeaderRow) {
                <i class="pi pi-check text-green-500"></i>
              } @else {
                <i class="pi pi-times text-[var(--p-text-muted-color)]"></i>
              }
            </td>
            <td class="text-sm text-[var(--p-text-muted-color)]">
              {{ profile.updatedAt | date:'dd/MM/yyyy HH:mm' }}
            </td>
            <td>
              <p-button
                icon="pi pi-pencil"
                severity="secondary"
                [text]="true"
                (onClick)="openEdit(profile)"
                ariaLabel="Editar"
              ></p-button>
              <p-button
                icon="pi pi-trash"
                severity="danger"
                [text]="true"
                (onClick)="confirmArchive(profile)"
                ariaLabel="Arquivar"
              ></p-button>
            </td>
          </tr>
        </ng-template>
        <ng-template pTemplate="emptymessage">
          <tr>
            <td colspan="5" class="text-center text-[var(--p-text-muted-color)]">
              Sem perfis de importação. Crie um perfil para reutilizar configurações de mapeamento.
            </td>
          </tr>
        </ng-template>
      </p-table>
      </div>

      <p-dialog
        [(visible)]="dialogOpen"
        [modal]="true"
        [closable]="true"
        [header]="editingId() ? 'Editar perfil' : 'Novo perfil'"
        [style]="{ width: '36rem' }"
        [breakpoints]="{ '960px': '75vw', '640px': '95vw' }"
      >
        <form [formGroup]="form" class="flex flex-col gap-4 pt-2" (ngSubmit)="submit()">
          <div class="flex flex-col gap-1">
            <label for="profile-name">Nome</label>
            <input id="profile-name" pInputText formControlName="name" maxlength="128" autocomplete="off" />
          </div>

          <div class="flex flex-col gap-1">
            <label for="profile-delimiter">Delimitador</label>
            <p-select
              inputId="profile-delimiter"
              [options]="delimiterOptions"
              optionLabel="label"
              optionValue="value"
              formControlName="delimiter"
              styleClass="w-full"
            ></p-select>
          </div>

          <div class="flex items-center gap-3">
            <p-checkbox
              inputId="profile-hasheader"
              [binary]="true"
              formControlName="hasHeaderRow"
            ></p-checkbox>
            <label for="profile-hasheader">Primeira linha é cabeçalho</label>
          </div>

          <div class="flex flex-col gap-1">
            <label for="profile-dateformat">Formato de data</label>
            <p-select
              inputId="profile-dateformat"
              [options]="dateFormatOptions"
              optionLabel="label"
              optionValue="value"
              formControlName="dateFormat"
              styleClass="w-full"
            ></p-select>
          </div>

          <div class="flex flex-col gap-1">
            <label for="profile-decseparator">Separador decimal</label>
            <p-select
              inputId="profile-decseparator"
              [options]="decimalSeparatorOptions"
              optionLabel="label"
              optionValue="value"
              formControlName="decimalSeparator"
              styleClass="w-full"
            ></p-select>
          </div>

          <div class="flex flex-col gap-1">
            <label for="profile-skiprows">Linhas a ignorar</label>
            <p-inputNumber
              inputId="profile-skiprows"
              formControlName="skipRows"
              [min]="0"
              styleClass="w-full"
            ></p-inputNumber>
          </div>

          <div class="flex flex-col gap-2">
            <div class="flex items-center justify-between">
              <label>Mapeamento de colunas</label>
              <p-button
                label="Adicionar"
                icon="pi pi-plus"
                size="small"
                severity="secondary"
                [text]="true"
                type="button"
                (onClick)="addMapping()"
              ></p-button>
            </div>

            <div class="overflow-x-auto border border-[var(--p-surface-300)] dark:border-[var(--p-surface-700)] rounded">
              <table class="w-full text-sm">
                <thead class="bg-[var(--p-surface-100)] dark:bg-[var(--p-surface-800)]">
                  <tr>
                    <th class="px-3 py-2 text-left">Coluna CSV</th>
                    <th class="px-3 py-2 text-left">Campo transação</th>
                    <th class="px-3 py-2 text-left" style="width:3rem"></th>
                  </tr>
                </thead>
                <tbody>
                  @for (mapping of mappings.controls; track i; let i = $index) {
                    <tr class="border-t border-[var(--p-surface-200)] dark:border-[var(--p-surface-700)]">
                      <td class="px-3 py-2">
                        <input pInputText [formControl]="$any(colName(mapping))" class="w-full text-sm" placeholder="Nome da coluna" />
                      </td>
                      <td class="px-3 py-2">
                        <p-select
                          [options]="fieldOptions"
                          optionLabel="label"
                          optionValue="value"
                          [formControl]="$any(colField(mapping))"
                          styleClass="w-full"
                        ></p-select>
                      </td>
                      <td class="px-3 py-2">
                        <button type="button"
                          class="text-[var(--p-text-muted-color)] hover:text-red-500"
                          (click)="removeMapping(i)">
                          <i class="pi pi-trash text-sm"></i>
                        </button>
                      </td>
                    </tr>
                  }
                  @if (mappings.length === 0) {
                    <tr>
                      <td colspan="3" class="px-3 py-3 text-center text-[var(--p-text-muted-color)] text-sm">
                        Sem mapeamentos. Adicione pelo menos um.
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </div>

          <div class="flex justify-end gap-2 pt-2">
            <p-button
              label="Cancelar"
              severity="secondary"
              [text]="true"
              type="button"
              (onClick)="close()"
            ></p-button>
            <p-button
              label="Guardar"
              icon="pi pi-check"
              type="submit"
              [disabled]="form.invalid || form.pristine || submitting()"
            ></p-button>
          </div>
        </form>
      </p-dialog>

      <p-confirmDialog></p-confirmDialog>
      <p-toast></p-toast>
    </div>
  `,
})
export class ImportProfilesPage implements OnInit {
  private readonly api = inject(FinancialApiService);
  private readonly fb = inject(FormBuilder);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);

  protected readonly profiles = signal<ImportProfileDto[]>([]);
  protected readonly delimiterOptions = DELIMITER_OPTIONS;
  protected readonly dateFormatOptions = DATE_FORMAT_OPTIONS;
  protected readonly decimalSeparatorOptions = DECIMAL_SEPARATOR_OPTIONS;
  protected readonly fieldOptions = Object.entries(TRANSACTION_FIELD_LABELS).map(([value, label]) => ({ value, label }));

  protected readonly dialogOpenSignal = signal(false);
  protected readonly editingId = signal<string | null>(null);
  protected readonly submitting = signal(false);

  protected get dialogOpen(): boolean { return this.dialogOpenSignal(); }
  protected set dialogOpen(value: boolean) {
    this.dialogOpenSignal.set(value);
    if (!value) this.editingId.set(null);
  }

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(128)]],
    delimiter: [',', Validators.required],
    hasHeaderRow: [true, Validators.required],
    dateFormat: ['yyyy-MM-dd', Validators.required],
    decimalSeparator: ['.', Validators.required],
    skipRows: [0, [Validators.required, Validators.min(0)]],
    mappings: this.fb.nonNullable.array<ColumnMappingDto>([]),
  });

  protected get mappings(): FormArray {
    return this.form.get('mappings') as FormArray;
  }

  async ngOnInit(): Promise<void> {
    try {
      await this.loadProfiles();
    } catch {
      this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Não foi possível carregar perfis.' });
    }
  }

  private async loadProfiles(): Promise<void> {
    this.profiles.set(await this.api.listImportProfiles());
  }

  protected openCreate(): void {
    this.editingId.set(null);
    this.mappings.clear();
    this.form.reset({
      name: '',
      delimiter: ',',
      hasHeaderRow: true,
      dateFormat: 'yyyy-MM-dd',
      decimalSeparator: '.',
      skipRows: 0,
      mappings: [],
    });
    this.dialogOpenSignal.set(true);
  }

  protected openEdit(profile: ImportProfileDto): void {
    this.editingId.set(profile.id);
    this.mappings.clear();
    for (const m of profile.columnMappings) {
      this.mappings.push(
        this.fb.nonNullable.group({
          csvColumnName: [m.csvColumnName, Validators.required],
          transactionField: [m.transactionField, Validators.required],
        }),
      );
    }
    this.form.reset({
      name: profile.name,
      delimiter: profile.delimiter,
      hasHeaderRow: profile.hasHeaderRow,
      dateFormat: profile.dateFormat,
      decimalSeparator: profile.decimalSeparator,
      skipRows: profile.skipRows,
      mappings: profile.columnMappings,
    });
    this.dialogOpenSignal.set(true);
  }

  protected close(): void { this.dialogOpen = false; }

  protected addMapping(): void {
    this.mappings.push(
      this.fb.nonNullable.group({
        csvColumnName: ['', Validators.required],
        transactionField: ['Description' as string, Validators.required],
      }),
    );
  }

  protected removeMapping(index: number): void {
    this.mappings.removeAt(index);
  }

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  protected colName(mapping: any): FormControl {
    return mapping.get('csvColumnName') as unknown as FormControl;
  }

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  protected colField(mapping: any): FormControl {
    return mapping.get('transactionField') as unknown as FormControl;
  }

  protected confirmArchive(profile: ImportProfileDto): void {
    this.confirm.confirm({
      message: `Arquivar o perfil "${profile.name}"?`,
      header: 'Arquivar perfil',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Arquivar',
      rejectLabel: 'Cancelar',
      accept: () => void this.archive(profile),
    });
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) return;
    this.submitting.set(true);
    try {
      const value = this.form.getRawValue();
      const mappings = value.mappings as ColumnMappingDto[];
      const id = this.editingId();
      if (id) {
        await this.api.updateImportProfile(id, {
          name: value.name,
          columnMappings: mappings,
          delimiter: value.delimiter,
          hasHeaderRow: value.hasHeaderRow,
          dateFormat: value.dateFormat,
          decimalSeparator: value.decimalSeparator,
          skipRows: value.skipRows,
        });
        this.toast.add({ severity: 'success', summary: 'Perfil atualizado' });
      } else {
        await this.api.createImportProfile({
          name: value.name,
          columnMappings: mappings,
          delimiter: value.delimiter || undefined,
          hasHeaderRow: value.hasHeaderRow,
          dateFormat: value.dateFormat || undefined,
          decimalSeparator: value.decimalSeparator || undefined,
          skipRows: value.skipRows || undefined,
        });
        this.toast.add({ severity: 'success', summary: 'Perfil criado' });
      }
      this.close();
      await this.loadProfiles();
    } catch {
      this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Não foi possível guardar o perfil.' });
    } finally {
      this.submitting.set(false);
    }
  }

  private async archive(profile: ImportProfileDto): Promise<void> {
    try {
      await this.api.archiveImportProfile(profile.id);
      this.toast.add({ severity: 'success', summary: 'Perfil arquivado' });
      await this.loadProfiles();
    } catch {
      this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Não foi possível arquivar o perfil.' });
    }
  }
}
