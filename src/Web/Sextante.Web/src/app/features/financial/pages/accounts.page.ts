import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { DialogModule } from 'primeng/dialog';
import { SelectModule } from 'primeng/select';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToastModule } from 'primeng/toast';
import { ConfirmationService, MessageService } from 'primeng/api';
import { FinancialApiService } from '../../../core/api/financial-api.service';
import {
  ACCOUNT_TYPES,
  ACCOUNT_TYPE_LABELS,
  AccountDto,
  AccountType,
} from '../../../core/api/financial.types';
import { MoneyPipe } from '../../../core/format/money.pipe';
import { FinancialStore } from '../state/financial.store';

@Component({
  selector: 'app-accounts-page',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    ButtonModule,
    DialogModule,
    SelectModule,
    InputNumberModule,
    InputTextModule,
    TableModule,
    TagModule,
    ToastModule,
    ConfirmDialogModule,
    MoneyPipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [MessageService, ConfirmationService],
  template: `
    <div class="max-w-5xl mx-auto">
      <div class="flex items-center justify-between mb-4">
        <h1 class="text-2xl font-semibold">Contas</h1>
        <p-button
          label="Nova conta"
          icon="pi pi-plus"
          (onClick)="openCreate()"
        ></p-button>
      </div>

      <p-table
        [value]="store.accounts()"
        [tableStyle]="{ 'min-width': '40rem' }"
        styleClass="p-datatable-sm"
        dataKey="id"
      >
        <ng-template pTemplate="header">
          <tr>
            <th>Nome</th>
            <th style="width: 12rem">Tipo</th>
            <th style="width: 12rem">Saldo inicial</th>
            <th style="width: 9rem"></th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-account>
          <tr>
            <td>{{ account.name }}</td>
            <td>
              <p-tag
                [value]="typeLabel(account.type)"
                severity="info"
                [rounded]="true"
              ></p-tag>
            </td>
            <td>{{ account.openingBalance | money }}</td>
            <td>
              <p-button
                icon="pi pi-pencil"
                severity="secondary"
                [text]="true"
                (onClick)="openEdit(account)"
                ariaLabel="Editar"
              ></p-button>
              <p-button
                icon="pi pi-trash"
                severity="danger"
                [text]="true"
                (onClick)="confirmArchive(account)"
                ariaLabel="Arquivar"
              ></p-button>
            </td>
          </tr>
        </ng-template>
        <ng-template pTemplate="emptymessage">
          <tr>
            <td colspan="4" class="text-center text-[var(--p-text-muted-color)]">
              Sem contas. Clique "Nova conta" para começar.
            </td>
          </tr>
        </ng-template>
      </p-table>

      <p-dialog
        [(visible)]="dialogOpen"
        [modal]="true"
        [closable]="true"
        [header]="editingId() ? 'Editar conta' : 'Nova conta'"
        [style]="{ width: '32rem' }"
      >
        <form [formGroup]="form" class="flex flex-col gap-4 pt-2" (ngSubmit)="submit()">
          <div class="flex flex-col gap-1">
            <label for="account-name">Nome</label>
            <input
              id="account-name"
              pInputText
              formControlName="name"
              maxlength="200"
              autocomplete="off"
            />
          </div>
          <div class="flex flex-col gap-1">
            <label for="account-type">Tipo</label>
            <p-select
              inputId="account-type"
              [options]="typeOptions"
              optionLabel="label"
              optionValue="value"
              formControlName="type"
              styleClass="w-full"
            ></p-select>
          </div>
          @if (!editingId()) {
            <div class="flex flex-col gap-1">
              <label for="account-balance">Saldo inicial</label>
              <p-inputNumber
                inputId="account-balance"
                mode="currency"
                currency="EUR"
                locale="pt-PT"
                [min]="0"
                formControlName="openingBalance"
              ></p-inputNumber>
            </div>
          }
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
export class AccountsPage implements OnInit {
  protected readonly store = inject(FinancialStore);
  private readonly api = inject(FinancialApiService);
  private readonly fb = inject(FormBuilder);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);

  protected readonly typeOptions = ACCOUNT_TYPES.map((value) => ({
    value,
    label: ACCOUNT_TYPE_LABELS[value],
  }));

  protected typeLabel(type: AccountType): string {
    return ACCOUNT_TYPE_LABELS[type];
  }

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
    name: ['', [Validators.required, Validators.maxLength(200)]],
    type: ['Checking' as AccountType, Validators.required],
    openingBalance: [0, [Validators.required, Validators.min(0)]],
  });

  async ngOnInit(): Promise<void> {
    try {
      await this.store.loadAccounts();
    } catch {
      this.toast.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível carregar contas.',
      });
    }
  }

  protected openCreate(): void {
    this.editingId.set(null);
    this.form.reset({ name: '', type: 'Checking', openingBalance: 0 });
    this.dialogOpenSignal.set(true);
  }

  protected openEdit(account: AccountDto): void {
    this.editingId.set(account.id);
    this.form.reset({
      name: account.name,
      type: account.type,
      openingBalance: account.openingBalance.amount,
    });
    this.dialogOpenSignal.set(true);
  }

  protected close(): void {
    this.dialogOpen = false;
  }

  protected confirmArchive(account: AccountDto): void {
    this.confirm.confirm({
      message: `Arquivar a conta "${account.name}"?`,
      header: 'Arquivar conta',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Arquivar',
      rejectLabel: 'Cancelar',
      accept: () => void this.archive(account.id),
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
        await this.api.updateAccount(id, { name: value.name, type: value.type });
        this.toast.add({ severity: 'success', summary: 'Conta atualizada' });
      } else {
        await this.api.createAccount({
          name: value.name,
          type: value.type,
          openingBalanceAmount: value.openingBalance,
        });
        this.toast.add({ severity: 'success', summary: 'Conta criada' });
      }
      this.close();
      await this.store.loadAccounts();
    } catch {
      this.toast.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível guardar a conta.',
      });
    } finally {
      this.submitting.set(false);
    }
  }

  private async archive(id: string): Promise<void> {
    try {
      await this.api.archiveAccount(id);
      this.toast.add({ severity: 'success', summary: 'Conta arquivada' });
      await this.store.loadAccounts();
    } catch {
      this.toast.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível arquivar a conta.',
      });
    }
  }
}
