import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { FinancialApiService } from '../../../core/api/financial-api.service';
import { ImportBatchDto } from '../../../core/api/financial.types';

const STATUS_SEVERITY: Record<string, 'success' | 'info' | 'warn' | 'danger'> = {
  Completed: 'success',
  Processing: 'info',
  Failed: 'danger',
  Pending: 'warn',
};

const STATUS_LABEL: Record<string, string> = {
  Completed: 'Concluída',
  Processing: 'A processar',
  Failed: 'Falhou',
  Pending: 'Pendente',
};

@Component({
  selector: 'app-import-batches-page',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    ButtonModule,
    TableModule,
    TagModule,
    ToastModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [MessageService],
  template: `
    <div class="max-w-5xl mx-auto">
      <div class="flex items-center justify-between flex-wrap gap-2 mb-4">
        <h1 class="text-xl md:text-2xl font-semibold">Importações</h1>
        <p-button
          label="Nova importação"
          icon="pi pi-upload"
          routerLink="/app/imports/new"
        ></p-button>
      </div>

      <div class="overflow-x-auto">
      <p-table
        [value]="batches()"
        [tableStyle]="{ 'min-width': '48rem' }"
        styleClass="p-datatable-sm"
        dataKey="id"
      >
        <ng-template pTemplate="header">
          <tr>
            <th>Ficheiro</th>
            <th style="width: 9rem">Estado</th>
            <th style="width: 7rem">Total</th>
            <th style="width: 7rem">Importadas</th>
            <th style="width: 7rem">Erros</th>
            <th style="width: 10rem">Data</th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-batch>
          <tr>
            <td class="font-medium">{{ batch.fileName }}</td>
            <td>
              <p-tag
                [value]="statusLabel(batch.status)"
                [severity]="statusSeverity(batch.status)"
                [rounded]="true"
              ></p-tag>
            </td>
            <td>{{ batch.totalRows }}</td>
            <td>{{ batch.importedRows }}</td>
            <td>
              @if (batch.errorRows > 0) {
                <span class="text-red-500 font-medium">{{ batch.errorRows }}</span>
              } @else {
                <span class="text-[var(--p-text-muted-color)]">0</span>
              }
            </td>
            <td class="text-sm text-[var(--p-text-muted-color)]">
              {{ batch.createdAt | date:'dd/MM/yyyy HH:mm' }}
            </td>
          </tr>
        </ng-template>
        <ng-template pTemplate="emptymessage">
          <tr>
            <td colspan="6" class="text-center text-[var(--p-text-muted-color)]">
              Sem importações recentes. Clique "Nova importação" para começar.
            </td>
          </tr>
        </ng-template>
      </p-table>
      </div>

      <p-toast></p-toast>
    </div>
  `,
})
export class ImportBatchesPage implements OnInit {
  private readonly api = inject(FinancialApiService);
  private readonly toast = inject(MessageService);

  protected readonly batches = signal<ImportBatchDto[]>([]);

  protected statusSeverity(status: string) {
    return STATUS_SEVERITY[status] ?? 'info';
  }

  protected statusLabel(status: string) {
    return STATUS_LABEL[status] ?? status;
  }

  async ngOnInit(): Promise<void> {
    try {
      this.batches.set(await this.api.listImportBatches());
    } catch {
      this.toast.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível carregar o histórico de importações.',
      });
    }
  }
}
