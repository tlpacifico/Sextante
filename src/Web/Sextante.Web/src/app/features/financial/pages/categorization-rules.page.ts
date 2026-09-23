import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
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
  CategorizationRuleDto,
  MATCH_TYPE_LABELS,
  MATCH_TYPES,
  MatchType,
  RULE_ACTION_LABELS,
  RuleAction,
} from '../../../core/api/financial.types';
import { FinancialStore } from '../state/financial.store';

@Component({
  selector: 'app-categorization-rules-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
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
        <h1 class="text-xl md:text-2xl font-semibold">Regras de categorização</h1>
        <p-button
          label="Nova regra"
          icon="pi pi-plus"
          (onClick)="openCreate()"
        ></p-button>
      </div>

      <div class="overflow-x-auto">
      <p-table
        [value]="rules()"
        [tableStyle]="{ 'min-width': '50rem' }"
        styleClass="p-datatable-sm"
        dataKey="id"
      >
        <ng-template pTemplate="header">
          <tr>
            <th style="width: 5rem">Prioridade</th>
            <th style="width: 5rem"></th>
            <th>Nome</th>
            <th>Padrão</th>
            <th style="width: 7rem">Tipo</th>
            <th>Ação</th>
            <th style="width: 5rem">Ativa</th>
            <th style="width: 9rem"></th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-rule let-i="rowIndex">
          <tr>
            <td>
              <div class="flex items-center gap-1">
                <span class="text-sm text-[var(--p-text-muted-color)]">#{{ rule.priority }}</span>
                <div class="flex flex-col">
                  <button
                    type="button"
                    class="text-xs text-[var(--p-text-muted-color)] hover:text-[var(--p-text-color)]"
                    [disabled]="i === 0"
                    (click)="moveRule(rule, -1)"
                  >
                    <i class="pi pi-chevron-up text-xs"></i>
                  </button>
                  <button
                    type="button"
                    class="text-xs text-[var(--p-text-muted-color)] hover:text-[var(--p-text-color)]"
                    [disabled]="i === rules().length - 1"
                    (click)="moveRule(rule, 1)"
                  >
                    <i class="pi pi-chevron-down text-xs"></i>
                  </button>
                </div>
              </div>
            </td>
            <td>
              @if (rule.isActive) {
                <span class="inline-block w-2 h-2 rounded-full bg-green-500"></span>
              } @else {
                <span class="inline-block w-2 h-2 rounded-full bg-[var(--p-surface-300)]"></span>
              }
            </td>
            <td>{{ rule.name }}</td>
            <td class="font-mono text-sm">{{ rule.pattern }}</td>
            <td>
              <p-tag [value]="matchTypeLabel(rule.matchType)" [rounded]="true" severity="info"></p-tag>
            </td>
            <td>
              @if (rule.action === 'MarkAsTransfer') {
                <span class="flex items-center gap-1">
                  <i class="pi pi-arrow-right-arrow-left"></i>
                  <span>Transferência → {{ rule.targetAccountName ?? '?' }}</span>
                </span>
              } @else {
                <span class="flex items-center gap-1">
                  <i class="pi {{ rule.categoryIcon }}" [style.color]="rule.categoryColor"></i>
                  <span>{{ rule.categoryName }}</span>
                </span>
              }
            </td>
            <td>
              <p-checkbox
                [binary]="true"
                [ngModel]="rule.isActive"
                (onChange)="toggleActive(rule)"
              ></p-checkbox>
            </td>
            <td>
              <p-button icon="pi pi-pencil" severity="secondary" [text]="true"
                (onClick)="openEdit(rule)" ariaLabel="Editar"></p-button>
              <p-button icon="pi pi-trash" severity="danger" [text]="true"
                (onClick)="confirmArchive(rule)" ariaLabel="Arquivar"></p-button>
            </td>
          </tr>
        </ng-template>
        <ng-template pTemplate="emptymessage">
          <tr>
            <td colspan="8" class="text-center text-[var(--p-text-muted-color)]">
              Sem regras de categorização. Crie regras para categorizar transações automaticamente.
            </td>
          </tr>
        </ng-template>
      </p-table>
      </div>

      <p-dialog
        [(visible)]="dialogOpen"
        [modal]="true"
        [closable]="true"
        [header]="editingId() ? 'Editar regra' : 'Nova regra'"
        [style]="{ width: '32rem' }"
        [breakpoints]="{ '960px': '75vw', '640px': '95vw' }"
      >
        <form [formGroup]="form" class="flex flex-col gap-4 pt-2" (ngSubmit)="submit()">
          <div class="flex flex-col gap-1">
            <label for="rule-name">Nome</label>
            <input id="rule-name" pInputText formControlName="name" maxlength="128" autocomplete="off" />
          </div>
          <div class="flex flex-col gap-1">
            <label for="rule-pattern">Padrão</label>
            <input id="rule-pattern" pInputText formControlName="pattern" maxlength="512" autocomplete="off" />
          </div>
          <div class="flex flex-col gap-1">
            <label for="rule-matchtype">Tipo de comparação</label>
            <p-select
              inputId="rule-matchtype"
              [options]="matchTypeOptions"
              optionLabel="label"
              optionValue="value"
              formControlName="matchType"
              styleClass="w-full"
            ></p-select>
          </div>
          <div class="flex flex-col gap-1">
            <label for="rule-action">Ação</label>
            <p-select
              inputId="rule-action"
              [options]="actionOptions"
              optionLabel="label"
              optionValue="value"
              formControlName="action"
              styleClass="w-full"
            ></p-select>
          </div>
          @if (isTransfer()) {
            <div class="flex flex-col gap-1">
              <label for="rule-target">Conta de destino</label>
              <p-select
                inputId="rule-target"
                [options]="accountOptions()"
                optionLabel="label"
                optionValue="value"
                formControlName="targetAccountId"
                styleClass="w-full"
                placeholder="Escolha a conta"
              ></p-select>
              <small class="text-[var(--p-text-muted-color)]">
                Movimentos que casam com o padrão passam a ser transferências para esta conta.
              </small>
            </div>
          } @else {
          <div class="flex flex-col gap-1">
            <label for="rule-category">Categoria alvo</label>
            <p-select
              inputId="rule-category"
              [options]="categoryOptions()"
              optionLabel="label"
              optionValue="value"
              formControlName="categoryId"
              styleClass="w-full"
            >
              <ng-template let-option pTemplate="item">
                <span class="flex items-center gap-2">
                  <i class="pi {{ option.icon }}"></i>
                  <span>{{ option.label }}</span>
                </span>
              </ng-template>
              <ng-template let-option pTemplate="selectedItem">
                <span class="flex items-center gap-2">
                  <i class="pi {{ option.icon }}"></i>
                  <span>{{ option.label }}</span>
                </span>
              </ng-template>
            </p-select>
          </div>
          }
          <div class="flex flex-col gap-1">
            <label for="rule-priority">Prioridade (menor = maior prioridade)</label>
            <p-inputNumber
              inputId="rule-priority"
              formControlName="priority"
              [min]="0"
              styleClass="w-full"
            ></p-inputNumber>
          </div>
          <div class="flex justify-end gap-2 pt-2">
            <p-button label="Cancelar" severity="secondary" [text]="true" type="button"
              (onClick)="close()"></p-button>
            <p-button label="Guardar" icon="pi pi-check" type="submit"
              [disabled]="form.invalid || submitting()"></p-button>
          </div>
        </form>
      </p-dialog>

      <p-confirmDialog></p-confirmDialog>
      <p-toast></p-toast>
    </div>
  `,
})
export class CategorizationRulesPage implements OnInit {
  protected readonly store = inject(FinancialStore);
  private readonly api = inject(FinancialApiService);
  private readonly fb = inject(FormBuilder);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);

  protected readonly rules = signal<CategorizationRuleDto[]>([]);
  protected readonly matchTypeOptions = MATCH_TYPES.map(t => ({ value: t, label: MATCH_TYPE_LABELS[t] }));
  protected readonly matchTypeLabel = (m: MatchType) => MATCH_TYPE_LABELS[m];
  protected readonly actionOptions = (Object.keys(RULE_ACTION_LABELS) as RuleAction[])
    .map(a => ({ value: a, label: RULE_ACTION_LABELS[a] }));

  protected readonly accountOptions = computed(() =>
    this.store.accounts().map(a => ({ value: a.id, label: a.name })));

  protected readonly categoryOptions = computed(() => {
    const cats = this.store.categories();
    if (!cats) return [];
    return cats.map(c => ({ value: c.id, label: c.name, icon: c.iconName }));
  });

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
    pattern: ['', [Validators.required, Validators.maxLength(512)]],
    matchType: ['Contains' as string, Validators.required],
    action: ['SetCategory' as RuleAction, Validators.required],
    categoryId: ['', Validators.required],
    targetAccountId: [{ value: '', disabled: true }, Validators.required],
    priority: [1, [Validators.required, Validators.min(0)]],
  });

  protected readonly isTransfer = signal(false);

  constructor() {
    // Phase 6.5 grupo 7 — categoria XOR conta de destino, conforme a ação.
    this.form.controls.action.valueChanges.subscribe(action => this.applyAction(action));
  }

  private applyAction(action: RuleAction): void {
    const transfer = action === 'MarkAsTransfer';
    this.isTransfer.set(transfer);
    if (transfer) {
      this.form.controls.categoryId.disable({ emitEvent: false });
      this.form.controls.targetAccountId.enable({ emitEvent: false });
    } else {
      this.form.controls.categoryId.enable({ emitEvent: false });
      this.form.controls.targetAccountId.disable({ emitEvent: false });
    }
  }

  async ngOnInit(): Promise<void> {
    try {
      await Promise.all([this.loadRules(), this.store.loadCategories(), this.store.loadAccounts()]);
    } catch {
      this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Não foi possível carregar dados.' });
    }
  }

  private async loadRules(): Promise<void> {
    const rules = await this.api.listCategorizationRules();
    this.rules.set(rules);
  }

  protected openCreate(): void {
    this.editingId.set(null);
    const nextPriority = this.rules().length > 0
      ? Math.max(...this.rules().map(r => r.priority)) + 1
      : 1;
    this.form.reset({
      name: '',
      pattern: '',
      matchType: 'Contains',
      action: 'SetCategory',
      categoryId: '',
      targetAccountId: '',
      priority: nextPriority,
    });
    this.applyAction('SetCategory');
    this.dialogOpenSignal.set(true);
  }

  protected openEdit(rule: CategorizationRuleDto): void {
    this.editingId.set(rule.id);
    this.form.reset({
      name: rule.name,
      pattern: rule.pattern,
      matchType: rule.matchType,
      action: rule.action,
      categoryId: rule.categoryId ?? '',
      targetAccountId: rule.targetAccountId ?? '',
      priority: rule.priority,
    });
    this.applyAction(rule.action);
    this.dialogOpenSignal.set(true);
  }

  protected close(): void { this.dialogOpen = false; }

  protected confirmArchive(rule: CategorizationRuleDto): void {
    this.confirm.confirm({
      message: `Arquivar a regra "${rule.name}"?`,
      header: 'Arquivar regra',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Arquivar',
      rejectLabel: 'Cancelar',
      accept: () => void this.archive(rule),
    });
  }

  protected async moveRule(rule: CategorizationRuleDto, direction: number): Promise<void> {
    const list = [...this.rules()];
    const idx = list.findIndex(r => r.id === rule.id);
    if (idx < 0) return;
    const newIdx = idx + direction;
    if (newIdx < 0 || newIdx >= list.length) return;

    [list[idx], list[newIdx]] = [list[newIdx], list[idx]];
    list.forEach((r, i) => r.priority = i + 1);
    this.rules.set(list);

    try {
      await this.api.reorderRules({ ruleIds: list.map(r => r.id) });
      await this.loadRules();
    } catch {
      this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Erro ao reordenar regras.' });
      await this.loadRules();
    }
  }

  protected async toggleActive(rule: CategorizationRuleDto): Promise<void> {
    try {
      await this.api.updateCategorizationRule(rule.id, {
        name: rule.name,
        pattern: rule.pattern,
        matchType: rule.matchType,
        categoryId: rule.categoryId,
        priority: rule.priority,
        isActive: !rule.isActive,
        action: rule.action,
        targetAccountId: rule.targetAccountId,
      });
      await this.loadRules();
    } catch {
      this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Erro ao alterar estado.' });
    }
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) return;
    this.submitting.set(true);
    try {
      const value = this.form.getRawValue();
      const transfer = value.action === 'MarkAsTransfer';
      const request = {
        name: value.name,
        pattern: value.pattern,
        matchType: value.matchType,
        categoryId: transfer ? null : value.categoryId,
        priority: value.priority,
        action: value.action,
        targetAccountId: transfer ? value.targetAccountId : null,
      };
      const id = this.editingId();
      if (id) {
        await this.api.updateCategorizationRule(id, { ...request, isActive: true });
        this.toast.add({ severity: 'success', summary: 'Regra atualizada' });
      } else {
        await this.api.createCategorizationRule(request);
        this.toast.add({ severity: 'success', summary: 'Regra criada' });
      }
      this.close();
      await this.loadRules();
    } catch {
      this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Não foi possível guardar a regra.' });
    } finally {
      this.submitting.set(false);
    }
  }

  private async archive(rule: CategorizationRuleDto): Promise<void> {
    try {
      await this.api.archiveCategorizationRule(rule.id);
      this.toast.add({ severity: 'success', summary: 'Regra arquivada' });
      await this.loadRules();
    } catch {
      this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Não foi possível arquivar a regra.' });
    }
  }
}
