import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { DialogModule } from 'primeng/dialog';
import { SelectModule } from 'primeng/select';
import { InputTextModule } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToastModule } from 'primeng/toast';
import { ConfirmationService, MessageService } from 'primeng/api';
import { FinancialApiService } from '../../../core/api/financial-api.service';
import {
  CATEGORY_KIND_LABELS,
  CategoryDto,
  CategoryKind,
} from '../../../core/api/financial.types';
import { CATEGORY_ICONS } from '../common/category-icons';
import { FinancialStore } from '../state/financial.store';

@Component({
  selector: 'app-categories-page',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    ButtonModule,
    DialogModule,
    SelectModule,
    InputTextModule,
    TableModule,
    TagModule,
    ToastModule,
    ConfirmDialogModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [MessageService, ConfirmationService],
  template: `
    <div class="max-w-5xl mx-auto">
      <div class="flex items-center justify-between mb-4">
        <h1 class="text-2xl font-semibold">Categorias</h1>
        <p-button
          label="Nova categoria"
          icon="pi pi-plus"
          (onClick)="openCreate()"
        ></p-button>
      </div>

      <p-table
        [value]="store.categories()"
        [tableStyle]="{ 'min-width': '40rem' }"
        styleClass="p-datatable-sm"
        dataKey="id"
      >
        <ng-template pTemplate="header">
          <tr>
            <th style="width: 4rem"></th>
            <th>Nome</th>
            <th style="width: 9rem">Tipo</th>
            <th style="width: 6rem">Cor</th>
            <th style="width: 9rem"></th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-category>
          <tr>
            <td>
              <i class="pi {{ category.iconName }}" [style.color]="category.colorHex"></i>
            </td>
            <td>{{ category.name }}</td>
            <td>
              <p-tag
                [value]="kindLabel(category.kind)"
                [severity]="category.kind === 'Expense' ? 'danger' : 'success'"
                [rounded]="true"
              ></p-tag>
            </td>
            <td>
              <span
                class="inline-block w-5 h-5 rounded-full border border-[var(--p-surface-300)] dark:border-[var(--p-surface-700)]"
                [style.background-color]="category.colorHex"
              ></span>
            </td>
            <td>
              <p-button
                icon="pi pi-pencil"
                severity="secondary"
                [text]="true"
                (onClick)="openEdit(category)"
                ariaLabel="Editar"
              ></p-button>
              <p-button
                icon="pi pi-trash"
                severity="danger"
                [text]="true"
                (onClick)="confirmArchive(category)"
                ariaLabel="Arquivar"
              ></p-button>
            </td>
          </tr>
        </ng-template>
        <ng-template pTemplate="emptymessage">
          <tr>
            <td colspan="5" class="text-center text-[var(--p-text-muted-color)]">
              Sem categorias. As categorias seed são criadas automaticamente no signup.
            </td>
          </tr>
        </ng-template>
      </p-table>

      <p-dialog
        [(visible)]="dialogOpen"
        [modal]="true"
        [closable]="true"
        [header]="editingId() ? 'Editar categoria' : 'Nova categoria'"
        [style]="{ width: '32rem' }"
      >
        <form [formGroup]="form" class="flex flex-col gap-4 pt-2" (ngSubmit)="submit()">
          <div class="flex flex-col gap-1">
            <label for="cat-name">Nome</label>
            <input
              id="cat-name"
              pInputText
              formControlName="name"
              maxlength="100"
              autocomplete="off"
            />
          </div>
          @if (!editingId()) {
            <div class="flex flex-col gap-1">
              <label for="cat-kind">Tipo</label>
              <p-select
                inputId="cat-kind"
                [options]="kindOptions"
                optionLabel="label"
                optionValue="value"
                formControlName="kind"
                styleClass="w-full"
              ></p-select>
            </div>
          }
          <div class="flex flex-col gap-1">
            <label for="cat-icon">Ícone</label>
            <p-select
              inputId="cat-icon"
              [options]="iconOptions"
              optionLabel="label"
              optionValue="name"
              formControlName="iconName"
              styleClass="w-full"
            >
              <ng-template let-option pTemplate="item">
                <span class="flex items-center gap-2">
                  <i class="pi {{ option.name }}"></i>
                  <span>{{ option.label }}</span>
                </span>
              </ng-template>
              <ng-template let-option pTemplate="selectedItem">
                <span class="flex items-center gap-2">
                  <i class="pi {{ option.name }}"></i>
                  <span>{{ option.label }}</span>
                </span>
              </ng-template>
            </p-select>
          </div>
          <div class="flex flex-col gap-1">
            <label for="cat-color">Cor</label>
            <input
              id="cat-color"
              type="color"
              formControlName="colorHex"
              class="h-9 rounded border border-[var(--p-surface-300)] dark:border-[var(--p-surface-700)]"
            />
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
              [disabled]="form.invalid || submitting()"
            ></p-button>
          </div>
        </form>
      </p-dialog>

      <p-confirmDialog></p-confirmDialog>
      <p-toast></p-toast>
    </div>
  `,
})
export class CategoriesPage implements OnInit {
  protected readonly store = inject(FinancialStore);
  private readonly api = inject(FinancialApiService);
  private readonly fb = inject(FormBuilder);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);

  protected readonly iconOptions = CATEGORY_ICONS;
  protected kindLabel(kind: CategoryKind): string {
    return CATEGORY_KIND_LABELS[kind];
  }
  protected readonly kindOptions = [
    { value: 'Expense', label: 'Despesa' },
    { value: 'Income', label: 'Receita' },
  ];

  protected readonly dialogOpenSignal = signal(false);
  protected readonly editingId = signal<string | null>(null);
  protected readonly submitting = signal(false);

  protected get dialogOpen(): boolean {
    return this.dialogOpenSignal();
  }
  protected set dialogOpen(value: boolean) {
    this.dialogOpenSignal.set(value);
    if (!value) {
      this.editingId.set(null);
    }
  }

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    kind: ['Expense' as CategoryKind, Validators.required],
    iconName: ['pi-tag', Validators.required],
    colorHex: ['#64748B', Validators.required],
  });

  async ngOnInit(): Promise<void> {
    try {
      await this.store.loadCategories();
    } catch {
      this.toast.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível carregar categorias.',
      });
    }
  }

  protected openCreate(): void {
    this.editingId.set(null);
    this.form.reset({
      name: '',
      kind: 'Expense',
      iconName: 'pi-tag',
      colorHex: '#64748B',
    });
    this.dialogOpenSignal.set(true);
  }

  protected openEdit(category: CategoryDto): void {
    this.editingId.set(category.id);
    this.form.reset({
      name: category.name,
      kind: category.kind,
      iconName: category.iconName,
      colorHex: category.colorHex,
    });
    this.dialogOpenSignal.set(true);
  }

  protected close(): void {
    this.dialogOpen = false;
  }

  protected confirmArchive(category: CategoryDto): void {
    this.confirm.confirm({
      message: `Arquivar a categoria "${category.name}"?`,
      header: 'Arquivar categoria',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Arquivar',
      rejectLabel: 'Cancelar',
      accept: () => void this.archive(category),
    });
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      return;
    }

    this.submitting.set(true);
    try {
      const value = this.form.getRawValue();
      const id = this.editingId();
      if (id) {
        await this.api.updateCategory(id, {
          name: value.name,
          iconName: value.iconName,
          colorHex: value.colorHex,
        });
        this.toast.add({ severity: 'success', summary: 'Categoria atualizada' });
      } else {
        await this.api.createCategory(value);
        this.toast.add({ severity: 'success', summary: 'Categoria criada' });
      }
      this.close();
      await this.store.loadCategories();
    } catch {
      this.toast.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível guardar a categoria.',
      });
    } finally {
      this.submitting.set(false);
    }
  }

  private async archive(category: CategoryDto): Promise<void> {
    try {
      await this.api.archiveCategory(category.id);
      this.toast.add({ severity: 'success', summary: 'Categoria arquivada' });
      await this.store.loadCategories();
    } catch (err) {
      const detail = this.extractError(err)
        ?? 'Não foi possível arquivar a categoria.';
      this.toast.add({ severity: 'error', summary: 'Erro', detail });
    }
  }

  private extractError(err: unknown): string | null {
    if (err instanceof HttpErrorResponse && err.status === 400) {
      return 'Não é possível arquivar uma categoria com transações ativas.';
    }
    return null;
  }
}
