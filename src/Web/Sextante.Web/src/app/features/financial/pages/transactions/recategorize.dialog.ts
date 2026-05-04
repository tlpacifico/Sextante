import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DialogModule } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { SelectModule } from 'primeng/select';
import { MessageService } from 'primeng/api';
import { FinancialApiService } from '../../../../core/api/financial-api.service';

@Component({
  selector: 'app-recategorize-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, SelectModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-dialog
      header="Recategorizar transações"
      [visible]="visible"
      [modal]="true"
      [draggable]="false"
      [breakpoints]="{ '960px': '75vw', '640px': '95vw' }"
      [style]="{ width: '24rem' }"
      (onHide)="close.emit()">
      <form [formGroup]="form" (ngSubmit)="submit()" class="flex flex-col gap-4 mt-2">
        <p class="text-sm text-neutral-500">
          {{ transactionIds.length }} transação(ões) selecionada(s).
        </p>

        <div class="flex flex-col gap-1">
          <label class="text-sm font-medium">Nova categoria</label>
          <p-select
            formControlName="categoryId"
            [options]="categories"
            optionLabel="name"
            optionValue="id"
            placeholder="Selecionar categoria"
            styleClass="w-full" />
        </div>

        <div class="flex justify-end gap-2 mt-2">
          <p-button label="Cancelar" severity="secondary" [outlined]="true" (click)="close.emit()" />
          <p-button type="submit" label="Recategorizar" [loading]="submitting()" [disabled]="submitting()" />
        </div>
      </form>
    </p-dialog>
  `,
})
export class RecategorizeDialogComponent {
  private readonly api = inject(FinancialApiService);
  private readonly fb = inject(FormBuilder);
  private readonly messages = inject(MessageService);

  @Input() visible = false;
  @Input() transactionIds: string[] = [];
  @Input() categories: { id: string; name: string }[] = [];
  @Output() close = new EventEmitter<void>();
  @Output() saved = new EventEmitter<void>();

  protected readonly submitting = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    categoryId: ['', Validators.required],
  });

  async submit(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    if (this.transactionIds.length === 0) return;

    this.submitting.set(true);
    try {
      const fv = this.form.getRawValue();
      const result = await this.api.recategorizeTransactions(this.transactionIds, fv.categoryId);
      this.messages.add({
        severity: 'success',
        summary: 'Recategorizadas',
        detail: `${result.updatedCount} transações recategorizadas.`,
        life: 3000,
      });
      this.close.emit();
      this.saved.emit();
    } catch {
      this.messages.add({ severity: 'error', summary: 'Erro', detail: 'Falha ao recategorizar.', life: 3000 });
    } finally {
      this.submitting.set(false);
    }
  }
}
