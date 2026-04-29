import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { DatePickerModule } from 'primeng/datepicker';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { ExchangeRatesApiService } from '../../core/api/exchange-rates-api.service';
import {
  ExchangeRateDto,
  ExchangeRateSnapshotStateDto,
} from '../../core/api/identity.types';
import { FinancialStore } from '../financial/state/financial.store';

@Component({
  selector: 'app-admin-exchange-rates-page',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    ButtonModule,
    CardModule,
    DatePickerModule,
    DialogModule,
    InputNumberModule,
    MessageModule,
    SelectModule,
    TableModule,
    ToastModule,
    DatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [MessageService],
  template: `
    <div class="max-w-5xl mx-auto flex flex-col gap-4">
      <header class="flex flex-wrap items-center justify-between gap-2">
        <h1 class="text-xl md:text-2xl font-semibold">Taxas de câmbio</h1>
        <div class="flex flex-wrap gap-2">
          <p-button
            label="Forçar snapshot ECB"
            icon="pi pi-refresh"
            severity="secondary"
            (onClick)="runSnapshot()"
            [loading]="snapshotting()"
          ></p-button>
          <p-button
            label="Inserir taxa manual"
            icon="pi pi-plus"
            (onClick)="openManual()"
          ></p-button>
        </div>
      </header>

      @if (state()?.lastError; as err) {
        <p-message
          severity="warn"
          [text]="err + ' (em ' + (state()?.lastRunAt | date: 'dd/MM/yyyy HH:mm') + ')'"
          data-testid="last-error-warning"
        ></p-message>
      }

      <p-card>
        <div class="overflow-x-auto">
          <p-table
            [value]="rates()"
            [tableStyle]="{ 'min-width': '40rem' }"
            styleClass="p-datatable-sm"
            dataKey="id"
          >
            <ng-template pTemplate="header">
              <tr>
                <th>Data</th>
                <th>De</th>
                <th>Para</th>
                <th style="text-align: right">Taxa</th>
                <th>Origem</th>
                <th>Atualizada</th>
              </tr>
            </ng-template>
            <ng-template pTemplate="body" let-rate>
              <tr>
                <td>{{ rate.rateDate }}</td>
                <td>{{ rate.fromCurrency }}</td>
                <td>{{ rate.toCurrency }}</td>
                <td style="text-align: right">{{ rate.rate }}</td>
                <td>{{ rate.source }}</td>
                <td>{{ rate.updatedAt | date: 'dd/MM/yyyy HH:mm' }}</td>
              </tr>
            </ng-template>
            <ng-template pTemplate="emptymessage">
              <tr>
                <td colspan="6" class="text-center text-[var(--p-text-muted-color)]">
                  Sem taxas para o período actual.
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
        header="Inserir taxa manual"
        [style]="{ width: '28rem' }"
        [breakpoints]="{ '960px': '75vw', '640px': '95vw' }"
      >
        <form [formGroup]="form" class="flex flex-col gap-4 pt-2" (ngSubmit)="submit()">
          <div class="flex flex-col gap-1">
            <label for="m-date">Data</label>
            <p-datepicker
              inputId="m-date"
              dateFormat="dd/mm/yy"
              formControlName="rateDate"
              [showIcon]="true"
              styleClass="w-full"
            ></p-datepicker>
          </div>
          <div class="flex flex-col gap-1">
            <label for="m-currency">Moeda</label>
            <p-select
              inputId="m-currency"
              [options]="store.currencies()"
              optionLabel="code"
              optionValue="code"
              formControlName="toCurrency"
              styleClass="w-full"
            ></p-select>
          </div>
          <div class="flex flex-col gap-1">
            <label for="m-rate">Taxa (EUR → moeda)</label>
            <p-inputNumber
              inputId="m-rate"
              [minFractionDigits]="2"
              [maxFractionDigits]="8"
              [min]="0.00000001"
              formControlName="rate"
              styleClass="w-full"
            ></p-inputNumber>
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
export class AdminExchangeRatesPage implements OnInit {
  protected readonly store = inject(FinancialStore);
  private readonly api = inject(ExchangeRatesApiService);
  private readonly fb = inject(FormBuilder);
  private readonly toast = inject(MessageService);

  protected readonly rates = signal<ExchangeRateDto[]>([]);
  protected readonly state = signal<ExchangeRateSnapshotStateDto | null>(null);
  protected readonly dialogOpenSignal = signal(false);
  protected readonly submitting = signal(false);
  protected readonly snapshotting = signal(false);

  protected get dialogOpen(): boolean {
    return this.dialogOpenSignal();
  }
  protected set dialogOpen(value: boolean) {
    this.dialogOpenSignal.set(value);
  }

  protected readonly form = this.fb.nonNullable.group({
    rateDate: [new Date() as Date | null, Validators.required],
    toCurrency: ['', Validators.required],
    rate: [1, [Validators.required, Validators.min(0.00000001)]],
  });

  async ngOnInit(): Promise<void> {
    await Promise.all([
      this.store.loadCurrencies(),
      this.refresh(),
    ]);
  }

  protected async refresh(): Promise<void> {
    try {
      const [rows, state] = await Promise.all([
        this.api.list(),
        this.api.getState(),
      ]);
      this.rates.set(rows);
      this.state.set(state);
    } catch {
      this.toast.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível carregar taxas de câmbio.',
      });
    }
  }

  protected async runSnapshot(): Promise<void> {
    this.snapshotting.set(true);
    try {
      await this.api.runSnapshot();
      this.toast.add({ severity: 'info', summary: 'Snapshot iniciado' });
      // Pequeno delay para deixar o job correr antes de refrescar.
      setTimeout(() => void this.refresh(), 2000);
    } catch {
      this.toast.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível iniciar o snapshot.',
      });
    } finally {
      this.snapshotting.set(false);
    }
  }

  protected openManual(): void {
    this.form.reset({
      rateDate: new Date(),
      toCurrency: '',
      rate: 1,
    });
    this.dialogOpenSignal.set(true);
  }

  protected close(): void {
    this.dialogOpenSignal.set(false);
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) return;
    this.submitting.set(true);
    try {
      const value = this.form.getRawValue();
      const date = value.rateDate as Date;
      const isoDate = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
      await this.api.insertManual({
        rateDate: isoDate,
        toCurrency: value.toCurrency,
        rate: value.rate,
      });
      this.toast.add({
        severity: 'success',
        summary: `Taxa manual registada para ${value.toCurrency} em ${isoDate}.`,
      });
      this.close();
      await this.refresh();
    } catch {
      this.toast.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível registar a taxa.',
      });
    } finally {
      this.submitting.set(false);
    }
  }
}
