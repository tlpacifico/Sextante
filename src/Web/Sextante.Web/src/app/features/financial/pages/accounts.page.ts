import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { DialogModule } from 'primeng/dialog';
import { SelectModule } from 'primeng/select';
import { DatePickerModule } from 'primeng/datepicker';
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
  CreditCardSettingsInput,
} from '../../../core/api/financial.types';
import { MoneyPipe } from '../../../core/format/money.pipe';
import { FinancialStore } from '../state/financial.store';
import { ReconcileAccountDialogComponent } from './reconcile-account.dialog';

@Component({
  selector: 'app-accounts-page',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    ButtonModule,
    DialogModule,
    SelectModule,
    DatePickerModule,
    InputNumberModule,
    InputTextModule,
    TableModule,
    TagModule,
    ToastModule,
    ConfirmDialogModule,
    MoneyPipe,
    ReconcileAccountDialogComponent,
    RouterLink,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [MessageService, ConfirmationService],
  template: `
    <div class="max-w-5xl mx-auto">
      <div class="flex items-center justify-between flex-wrap gap-2 mb-4">
        <h1 class="text-xl md:text-2xl font-semibold">Contas</h1>
        <p-button
          label="Nova conta"
          icon="pi pi-plus"
          (onClick)="openCreate()"
        ></p-button>
      </div>

      <div class="overflow-x-auto">
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
            <th style="width: 12rem">Saldo atual</th>
            <th style="width: 14rem"></th>
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
            <td [class.text-red-600]="account.currentBalance.amount < 0" [class.dark:text-red-400]="account.currentBalance.amount < 0">
              @if (account.type === 'CreditCard' && account.currentBalance.amount < 0) {
                <span class="text-xs text-[var(--p-text-muted-color)] mr-1">Dívida</span>
              }
              {{ account.currentBalance | money }}
            </td>
            <td class="whitespace-nowrap">
              @if (account.type === 'CreditCard') {
                <a
                  [routerLink]="['/app/accounts', account.id, 'credit-card']"
                  class="p-button p-button-text p-button-secondary p-button-icon-only"
                  aria-label="Ver cartão"
                >
                  <i class="pi pi-credit-card"></i>
                </a>
              }
              <p-button
                icon="pi pi-check-square"
                severity="secondary"
                [text]="true"
                (onClick)="reconcileAccount.set(account)"
                ariaLabel="Reconciliar"
              ></p-button>
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
            <td colspan="5" class="text-center text-[var(--p-text-muted-color)]">
              Sem contas. Clique "Nova conta" para começar.
            </td>
          </tr>
        </ng-template>
      </p-table>
      </div>

      <app-reconcile-account-dialog
        [visible]="reconcileAccount() !== null"
        [account]="reconcileAccount()"
        (close)="reconcileAccount.set(null)"
        (saved)="onReconciled()"
      />

      <p-dialog
        [(visible)]="dialogOpen"
        [modal]="true"
        [closable]="true"
        [header]="editingId() ? 'Editar conta' : 'Nova conta'"
        [style]="{ width: '32rem' }"
        [breakpoints]="{ '960px': '75vw', '640px': '95vw' }"
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
          <div class="flex flex-col gap-1">
            <label for="account-currency">Moeda</label>
            <p-select
              inputId="account-currency"
              [options]="store.currencies()"
              optionLabel="code"
              optionValue="code"
              formControlName="currency"
              styleClass="w-full"
              [disabled]="!!editingId()"
            ></p-select>
          </div>
          @if (!editingId()) {
            <div class="flex flex-col gap-1">
              <label for="account-balance">Saldo inicial</label>
              <p-inputNumber
                inputId="account-balance"
                mode="currency"
                [currency]="form.controls.currency.value || 'EUR'"
                locale="pt-PT"
                [min]="form.controls.type.value === 'CreditCard' ? null : 0"
                formControlName="openingBalance"
              ></p-inputNumber>
            </div>
            <div class="flex flex-col gap-1">
              <label for="account-balance-date">Data do saldo inicial</label>
              <p-datepicker
                inputId="account-balance-date"
                formControlName="openingBalanceDate"
                dateFormat="dd/mm/yy"
                styleClass="w-full"
                [maxDate]="today"
              ></p-datepicker>
              <small class="text-[var(--p-text-muted-color)]">Não pode ser alterada depois de criar a conta.</small>
            </div>
          }
          @if (form.controls.type.value === 'CreditCard') {
            <fieldset class="flex flex-col gap-3 border-t border-[var(--p-content-border-color)] pt-3">
              <legend class="text-sm font-medium pr-2">Definições do cartão</legend>
              <div class="flex flex-col gap-1">
                <label for="account-credit-limit">Limite de crédito</label>
                <p-inputNumber
                  inputId="account-credit-limit"
                  mode="currency"
                  [currency]="form.controls.currency.value || 'EUR'"
                  locale="pt-PT"
                  [min]="0"
                  formControlName="creditLimit"
                ></p-inputNumber>
              </div>
              <div class="grid grid-cols-2 gap-3">
                <div class="flex flex-col gap-1">
                  <label for="account-closing-day">Dia de fecho</label>
                  <p-inputNumber
                    inputId="account-closing-day"
                    [min]="1"
                    [max]="31"
                    [useGrouping]="false"
                    formControlName="statementClosingDay"
                  ></p-inputNumber>
                </div>
                <div class="flex flex-col gap-1">
                  <label for="account-due-day">Dia de pagamento</label>
                  <p-inputNumber
                    inputId="account-due-day"
                    [min]="1"
                    [max]="31"
                    [useGrouping]="false"
                    formControlName="paymentDueDay"
                  ></p-inputNumber>
                </div>
              </div>
              <small class="text-[var(--p-text-muted-color)]">
                Se o dia não existir no mês, conta o dia 1 do mês seguinte. O pagamento é no mês a seguir ao fecho quando o dia de pagamento não é posterior ao de fecho.
              </small>
              <div class="flex flex-col gap-1">
                <label for="account-payment-account">Conta de pagamento</label>
                <p-select
                  inputId="account-payment-account"
                  [options]="paymentAccountOptions()"
                  optionLabel="name"
                  optionValue="id"
                  [showClear]="true"
                  placeholder="Nenhuma"
                  formControlName="paymentAccountId"
                  styleClass="w-full"
                ></p-select>
              </div>
            </fieldset>
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

  // Data do saldo inicial não pode ser no futuro — regra de domínio
  // (Account.Create). O datepicker já não oferece essas datas.
  protected readonly today = new Date();

  protected readonly dialogOpenSignal = signal(false);
  protected readonly editingId = signal<string | null>(null);
  protected readonly submitting = signal(false);
  protected readonly reconcileAccount = signal<AccountDto | null>(null);

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
    currency: ['EUR' as string, Validators.required],
    openingBalance: [0, [Validators.required, Validators.min(0)]],
    openingBalanceDate: [new Date(), Validators.required],
    // Phase 6.5 grupo 5 — só obrigatórios quando o tipo é Cartão de crédito.
    creditLimit: [null as number | null],
    statementClosingDay: [null as number | null],
    paymentDueDay: [null as number | null],
    paymentAccountId: [null as string | null],
  });

  /** Contas elegíveis para pagar o cartão: não cartões e não a própria. */
  protected readonly paymentAccountOptions = computed(() =>
    this.store.accounts().filter((a) => a.type !== 'CreditCard' && a.id !== this.editingId()),
  );

  async ngOnInit(): Promise<void> {
    // CreditCard aceita saldo inicial negativo (dívida); os restantes tipos
    // continuam a exigir >= 0 — reaplica o validador sempre que o tipo muda.
    this.form.controls.type.valueChanges.subscribe((type) => {
      const balanceControl = this.form.controls.openingBalance;
      balanceControl.setValidators(
        type === 'CreditCard'
          ? [Validators.required]
          : [Validators.required, Validators.min(0)],
      );
      balanceControl.updateValueAndValidity();
      this.applyCreditCardValidators(type);
    });

    try {
      await Promise.all([
        this.store.loadAccounts(),
        this.store.loadCurrencies(),
        this.store.loadTenantSettings(),
      ]);
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
    const defaultCurrency =
      this.store.tenantSettings()?.primaryCurrency ?? 'EUR';
    this.form.reset({
      name: '',
      type: 'Checking',
      currency: defaultCurrency,
      openingBalance: 0,
      openingBalanceDate: new Date(),
      creditLimit: null,
      statementClosingDay: null,
      paymentDueDay: null,
      paymentAccountId: null,
    });
    this.dialogOpenSignal.set(true);
  }

  protected openEdit(account: AccountDto): void {
    this.editingId.set(account.id);
    this.form.reset({
      name: account.name,
      type: account.type,
      currency: account.currency,
      openingBalance: account.openingBalance.amount,
      creditLimit: account.creditCard?.creditLimit.amount ?? null,
      statementClosingDay: account.creditCard?.statementClosingDay ?? null,
      paymentDueDay: account.creditCard?.paymentDueDay ?? null,
      paymentAccountId: account.creditCard?.paymentAccountId ?? null,
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
        await this.api.updateAccount(id, {
          name: value.name,
          type: value.type,
          creditCard: this.creditCardInput(),
        });
        this.toast.add({ severity: 'success', summary: 'Conta atualizada' });
      } else {
        await this.api.createAccount({
          name: value.name,
          type: value.type,
          currency: value.currency,
          openingBalanceAmount: value.openingBalance,
          openingBalanceDate: this.toDateString(value.openingBalanceDate),
          creditCard: this.creditCardInput(),
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

  private applyCreditCardValidators(type: AccountType): void {
    const { creditLimit, statementClosingDay, paymentDueDay, paymentAccountId } = this.form.controls;
    if (type === 'CreditCard') {
      creditLimit.setValidators([Validators.required, Validators.min(0.01)]);
      statementClosingDay.setValidators([Validators.required, Validators.min(1), Validators.max(31)]);
      paymentDueDay.setValidators([Validators.required, Validators.min(1), Validators.max(31)]);
    } else {
      for (const control of [creditLimit, statementClosingDay, paymentDueDay, paymentAccountId]) {
        control.clearValidators();
        control.setValue(null, { emitEvent: false });
      }
    }
    for (const control of [creditLimit, statementClosingDay, paymentDueDay]) {
      control.updateValueAndValidity({ emitEvent: false });
    }
  }

  /** Só com o tipo Cartão e os três campos obrigatórios preenchidos. */
  private creditCardInput(): CreditCardSettingsInput | null {
    const value = this.form.getRawValue();
    if (value.type !== 'CreditCard' || value.creditLimit === null
      || value.statementClosingDay === null || value.paymentDueDay === null) {
      return null;
    }
    return {
      creditLimit: value.creditLimit,
      statementClosingDay: value.statementClosingDay,
      paymentDueDay: value.paymentDueDay,
      paymentAccountId: value.paymentAccountId,
    };
  }

  protected async onReconciled(): Promise<void> {
    this.reconcileAccount.set(null);
    await this.store.loadAccounts();
  }

  private toDateString(date: Date): string {
    const y = date.getFullYear();
    const m = String(date.getMonth() + 1).padStart(2, '0');
    const d = String(date.getDate()).padStart(2, '0');
    return `${y}-${m}-${d}`;
  }

  private async archive(id: string): Promise<void> {
    try {
      await this.api.archiveAccount(id);
      this.toast.add({ severity: 'success', summary: 'Conta arquivada' });
      await this.store.loadAccounts();
    } catch (err) {
      // 400 = conta com transações ativas (regra de domínio, Phase 6.5).
      const detail = err instanceof HttpErrorResponse && err.status === 400
        ? 'Não é possível arquivar uma conta com transações ativas.'
        : 'Não foi possível arquivar a conta.';
      this.toast.add({ severity: 'error', summary: 'Erro', detail });
    }
  }
}
