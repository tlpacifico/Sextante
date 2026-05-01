import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { DialogModule } from 'primeng/dialog';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { ToastModule } from 'primeng/toast';
import { ConfirmationService, MessageService } from 'primeng/api';
import { FinancialApiService } from '../../../core/api/financial-api.service';
import {
  FREQUENCY_LABELS,
  Frequency,
  RecurringRuleDto,
} from '../../../core/api/financial.types';
import { MoneyPipe } from '../../../core/format/money.pipe';
import { RecurringRuleDialogComponent } from './recurring-rule.dialog';
import { UpcomingOccurrencesDialogComponent } from './upcoming-occurrences.dialog';

@Component({
  selector: 'app-recurring-rules-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    ButtonModule,
    ConfirmDialogModule,
    DialogModule,
    TableModule,
    TagModule,
    ToastModule,
    ToggleSwitchModule,
    MoneyPipe,
    RecurringRuleDialogComponent,
    UpcomingOccurrencesDialogComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [MessageService, ConfirmationService],
  template: `
    <div class="max-w-5xl mx-auto">
      <div class="flex items-center justify-between flex-wrap gap-2 mb-4">
        <h1 class="text-xl md:text-2xl font-semibold">Recorrentes</h1>
        <p-button
          label="Nova regra"
          icon="pi pi-plus"
          (onClick)="openCreate()"
        ></p-button>
      </div>

      <div class="overflow-x-auto">
      <p-table
        [value]="rules()"
        [tableStyle]="{ 'min-width': '48rem' }"
        styleClass="p-datatable-sm"
        dataKey="id"
      >
        <ng-template pTemplate="header">
          <tr>
            <th>Descrição</th>
            <th style="width: 8rem">Valor</th>
            <th style="width: 7rem">Frequência</th>
            <th style="width: 8rem">Próxima</th>
            <th style="width: 5rem">Ativa</th>
            <th style="width: 10rem"></th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-rule>
          <tr [class.opacity-50]="!rule.nextOccurrence">
            <td>{{ rule.description }}</td>
            <td>{{ rule.amount | money }}</td>
            <td>
              @if (rule.interval > 1) {
                <p-tag
                  [value]="'A cada ' + rule.interval + ' ' + freqLabel(rule.frequency).toLowerCase() + 's'"
                  severity="info"
                  [rounded]="true"
                ></p-tag>
              } @else {
                <p-tag
                  [value]="freqLabel(rule.frequency)"
                  severity="info"
                  [rounded]="true"
                ></p-tag>
              }
            </td>
            <td>
              @if (rule.nextOccurrence; as next) {
                {{ next | date:'dd/MM/yyyy' }}
              } @else {
                <p-tag value="Concluída" severity="secondary" [rounded]="true"></p-tag>
              }
            </td>
            <td>
              <p-toggleswitch
                [(ngModel)]="rule.isActive"
                (onChange)="toggleActive(rule)"
                [disabled]="!rule.nextOccurrence"
              ></p-toggleswitch>
            </td>
            <td>
              <p-button
                icon="pi pi-pencil"
                severity="secondary"
                [text]="true"
                (onClick)="openEdit(rule)"
                ariaLabel="Editar"
              ></p-button>
              <p-button
                icon="pi pi-calendar-clock"
                severity="secondary"
                [text]="true"
                (onClick)="openUpcoming(rule)"
                ariaLabel="Ver próximas"
              ></p-button>
              <p-button
                icon="pi pi-trash"
                severity="danger"
                [text]="true"
                (onClick)="confirmArchive(rule)"
                ariaLabel="Apagar"
              ></p-button>
            </td>
          </tr>
        </ng-template>
        <ng-template pTemplate="emptymessage">
          <tr>
            <td colspan="6" class="text-center text-[var(--p-text-muted-color)]">
              Sem regras recorrentes. Cria a primeira para automatizar transações.
            </td>
          </tr>
        </ng-template>
      </p-table>
      </div>

      @if (dialogRuleId(); as id) {
        <app-recurring-rule-dialog
          [editingId]="editingRuleId()"
          (saved)="onRuleSaved()"
          (closed)="closeDialog()"
        ></app-recurring-rule-dialog>
      }

      @if (upcomingRule(); as rule) {
        <app-upcoming-occurrences-dialog
          [ruleId]="rule.id"
          [ruleDescription]="rule.description"
          (closed)="upcomingRule.set(null)"
        ></app-upcoming-occurrences-dialog>
      }

      <p-confirmDialog></p-confirmDialog>
      <p-toast></p-toast>
    </div>
  `,
})
export class RecurringRulesPage implements OnInit {
  private readonly api = inject(FinancialApiService);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);

  protected readonly rules = signal<RecurringRuleDto[]>([]);
  protected readonly dialogRuleId = signal<string | null>(null);
  protected readonly editingRuleId = signal<string | null>(null);
  protected readonly upcomingRule = signal<RecurringRuleDto | null>(null);

  async ngOnInit(): Promise<void> {
    await this.loadRules();
  }

  private async loadRules(): Promise<void> {
    try {
      const list = await this.api.listRecurringRules();
      this.rules.set(list);
    } catch {
      this.toast.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível carregar regras recorrentes.',
      });
    }
  }

  protected freqLabel(freq: Frequency): string {
    return FREQUENCY_LABELS[freq] ?? freq;
  }

  protected openCreate(): void {
    this.editingRuleId.set(null);
    this.dialogRuleId.set('new');
  }

  protected openEdit(rule: RecurringRuleDto): void {
    this.editingRuleId.set(rule.id);
    this.dialogRuleId.set(rule.id);
  }

  protected closeDialog(): void {
    this.dialogRuleId.set(null);
    this.editingRuleId.set(null);
  }

  protected onRuleSaved(): void {
    this.closeDialog();
    void this.loadRules();
  }

  protected openUpcoming(rule: RecurringRuleDto): void {
    this.upcomingRule.set(rule);
  }

  protected async toggleActive(rule: RecurringRuleDto): Promise<void> {
    try {
      await this.api.updateRecurringRule(rule.id, {
        description: rule.description,
        amount: rule.amount.amount,
        currency: rule.amount.currency,
        accountId: rule.accountId,
        categoryId: rule.categoryId,
        frequency: rule.frequency,
        interval: rule.interval,
        startDate: rule.startDate,
        endDate: rule.endDate,
        isActive: rule.isActive,
        tags: rule.tags,
      });
      this.toast.add({ severity: 'success', summary: 'Regra atualizada' });
    } catch {
      rule.isActive = !rule.isActive;
      this.toast.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível atualizar a regra.',
      });
    }
  }

  protected confirmArchive(rule: RecurringRuleDto): void {
    this.confirm.confirm({
      message: `Apagar a regra "${rule.description}"?`,
      header: 'Apagar regra',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Apagar',
      rejectLabel: 'Cancelar',
      accept: () => void this.archive(rule.id),
    });
  }

  private async archive(id: string): Promise<void> {
    try {
      await this.api.archiveRecurringRule(id);
      this.toast.add({ severity: 'success', summary: 'Regra apagada' });
      await this.loadRules();
    } catch {
      this.toast.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível apagar a regra.',
      });
    }
  }
}
