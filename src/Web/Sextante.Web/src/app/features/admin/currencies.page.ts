import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToastModule } from 'primeng/toast';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { MessageService } from 'primeng/api';
import { CurrencyApiService } from '../../core/api/currency-api.service';
import { CurrencyDto } from '../../core/api/identity.types';

@Component({
  selector: 'app-admin-currencies-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    ButtonModule,
    CardModule,
    DialogModule,
    InputNumberModule,
    InputTextModule,
    TableModule,
    TagModule,
    ToastModule,
    ToggleSwitchModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [MessageService],
  template: `
    <div class="max-w-5xl mx-auto flex flex-col gap-4">
      <header class="flex flex-wrap items-center justify-between gap-2">
        <h1 class="text-xl md:text-2xl font-semibold">Moedas</h1>
        <p-button label="Nova moeda" icon="pi pi-plus" (onClick)="openCreate()"></p-button>
      </header>

      <p-card>
        <div class="overflow-x-auto">
          <p-table
            [value]="currencies()"
            [tableStyle]="{ 'min-width': '40rem' }"
            styleClass="p-datatable-sm"
            dataKey="code"
          >
            <ng-template pTemplate="header">
              <tr>
                <th style="width: 6rem">Código</th>
                <th>Nome</th>
                <th style="width: 6rem">Símbolo</th>
                <th style="width: 8rem; text-align: right">Casas decimais</th>
                <th style="width: 7rem">Activa</th>
                <th style="width: 6rem"></th>
              </tr>
            </ng-template>
            <ng-template pTemplate="body" let-currency>
              <tr>
                <td>{{ currency.code }}</td>
                <td>{{ currency.name }}</td>
                <td>{{ currency.symbol }}</td>
                <td style="text-align: right">{{ currency.minorUnits }}</td>
                <td>
                  <p-toggleswitch
                    [ngModel]="currency.isActive"
                    (ngModelChange)="toggleActive(currency, $event)"
                  ></p-toggleswitch>
                </td>
                <td>
                  <p-button
                    icon="pi pi-pencil"
                    severity="secondary"
                    [text]="true"
                    (onClick)="openEdit(currency)"
                    ariaLabel="Editar"
                  ></p-button>
                </td>
              </tr>
            </ng-template>
            <ng-template pTemplate="emptymessage">
              <tr>
                <td colspan="6" class="text-center text-[var(--p-text-muted-color)]">
                  Sem moedas.
                </td>
              </tr>
            </ng-template>
          </p-table>
        </div>
      </p-card>

      <p-dialog
        [(visible)]="dialogOpen"
        [modal]="true"
        [closable]="true"
        [header]="editing() ? 'Editar moeda' : 'Nova moeda'"
        [style]="{ width: '28rem' }"
        [breakpoints]="{ '960px': '75vw', '640px': '95vw' }"
      >
        <form [formGroup]="form" class="flex flex-col gap-4 pt-2" (ngSubmit)="submit()">
          <div class="flex flex-col gap-1">
            <label for="c-code">Código (ISO 4217)</label>
            <input
              id="c-code"
              pInputText
              formControlName="code"
              maxlength="3"
              autocomplete="off"
              [readonly]="!!editing()"
            />
          </div>
          <div class="flex flex-col gap-1">
            <label for="c-name">Nome</label>
            <input id="c-name" pInputText formControlName="name" maxlength="120" />
          </div>
          <div class="flex flex-col gap-1">
            <label for="c-symbol">Símbolo</label>
            <input id="c-symbol" pInputText formControlName="symbol" maxlength="16" />
          </div>
          <div class="flex flex-col gap-1">
            <label for="c-minor">Casas decimais</label>
            <p-inputNumber
              inputId="c-minor"
              [min]="0"
              [max]="6"
              formControlName="minorUnits"
              styleClass="w-full"
            ></p-inputNumber>
          </div>
          <div class="flex items-center gap-3">
            <p-toggleswitch formControlName="isActive"></p-toggleswitch>
            <span>Activa</span>
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

      <p-toast></p-toast>
    </div>
  `,
})
export class AdminCurrenciesPage implements OnInit {
  private readonly api = inject(CurrencyApiService);
  private readonly fb = inject(FormBuilder);
  private readonly toast = inject(MessageService);

  protected readonly currencies = signal<CurrencyDto[]>([]);
  protected readonly dialogOpenSignal = signal(false);
  protected readonly editing = signal<string | null>(null);
  protected readonly submitting = signal(false);

  protected get dialogOpen(): boolean {
    return this.dialogOpenSignal();
  }
  protected set dialogOpen(value: boolean) {
    this.dialogOpenSignal.set(value);
    if (!value) this.editing.set(null);
  }

  protected readonly form = this.fb.nonNullable.group({
    code: ['', [Validators.required, Validators.pattern(/^[A-Z]{3}$/)]],
    name: ['', [Validators.required, Validators.maxLength(120)]],
    symbol: ['', Validators.maxLength(16)],
    minorUnits: [2, [Validators.required, Validators.min(0), Validators.max(6)]],
    isActive: [true],
  });

  async ngOnInit(): Promise<void> {
    await this.refresh();
  }

  protected async refresh(): Promise<void> {
    try {
      this.currencies.set(await this.api.listAll());
    } catch {
      this.toast.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível carregar moedas.',
      });
    }
  }

  protected openCreate(): void {
    this.editing.set(null);
    this.form.reset({
      code: '',
      name: '',
      symbol: '',
      minorUnits: 2,
      isActive: true,
    });
    this.dialogOpenSignal.set(true);
  }

  protected openEdit(currency: CurrencyDto): void {
    this.editing.set(currency.code);
    this.form.reset({
      code: currency.code,
      name: currency.name,
      symbol: currency.symbol,
      minorUnits: currency.minorUnits,
      isActive: currency.isActive,
    });
    this.dialogOpenSignal.set(true);
  }

  protected close(): void {
    this.dialogOpen = false;
  }

  protected async toggleActive(currency: CurrencyDto, isActive: boolean): Promise<void> {
    try {
      await this.api.update(currency.code, {
        name: currency.name,
        symbol: currency.symbol,
        minorUnits: currency.minorUnits,
        isActive,
      });
      this.toast.add({
        severity: 'success',
        summary: isActive ? `Moeda ${currency.code} activada.` : `Moeda ${currency.code} desactivada.`,
      });
      await this.refresh();
    } catch {
      this.toast.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível alterar a moeda.',
      });
    }
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) return;
    this.submitting.set(true);
    try {
      const value = this.form.getRawValue();
      const code = this.editing();
      if (code) {
        await this.api.update(code, {
          name: value.name,
          symbol: value.symbol,
          minorUnits: value.minorUnits,
          isActive: value.isActive,
        });
        this.toast.add({ severity: 'success', summary: 'Moeda atualizada' });
      } else {
        await this.api.create({
          code: value.code.toUpperCase(),
          name: value.name,
          symbol: value.symbol,
          minorUnits: value.minorUnits,
          isActive: value.isActive,
        });
        this.toast.add({ severity: 'success', summary: 'Moeda criada' });
      }
      this.close();
      await this.refresh();
    } catch {
      this.toast.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível guardar a moeda.',
      });
    } finally {
      this.submitting.set(false);
    }
  }
}
