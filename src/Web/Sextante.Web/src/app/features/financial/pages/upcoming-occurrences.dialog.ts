import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { TagModule } from 'primeng/tag';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { FinancialApiService } from '../../../core/api/financial-api.service';

@Component({
  selector: 'app-upcoming-occurrences-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ButtonModule,
    DialogModule,
    TagModule,
    ProgressSpinnerModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-dialog
      [(visible)]="visible"
      [modal]="true"
      [closable]="true"
      [header]="'Próximas ocorrências: ' + ruleDescription"
      [style]="{ width: '28rem' }"
      [breakpoints]="{ '960px': '75vw', '640px': '95vw' }"
      (onHide)="closed.emit()"
    >
      @if (loading()) {
        <div class="flex justify-center p-6">
          <p-progressSpinner styleClass="w-8 h-8"></p-progressSpinner>
        </div>
      } @else if (dates().length === 0) {
        <div class="text-center text-[var(--p-text-muted-color)] p-4">
          Não há ocorrências futuras previstas.
        </div>
      } @else {
        <ul class="flex flex-col gap-2 pt-2">
          @for (date of dates(); track date; let i = $index) {
            <li class="flex items-center gap-3 px-3 py-2 rounded-md
                       bg-[var(--p-surface-100)]
                       dark:bg-[var(--p-surface-800)]">
              <span class="text-sm">{{ date | date:'dd/MM/yyyy' : '+0000' }}</span>
              @if (i === 0) {
                <p-tag value="Próxima" severity="success" [rounded]="true"></p-tag>
              }
            </li>
          }
        </ul>
        @if (dates().length < 10) {
          <div class="mt-3 text-sm text-[var(--p-text-muted-color)] text-center">
            Regra termina antes de 10 ocorrências futuras.
          </div>
        }
      }
    </p-dialog>
  `,
})
export class UpcomingOccurrencesDialogComponent implements OnInit {
  @Input() ruleId!: string;
  @Input() ruleDescription!: string;
  @Output() closed = new EventEmitter<void>();

  private readonly api = inject(FinancialApiService);

  protected readonly visible = true;
  protected readonly loading = signal(true);
  protected readonly dates = signal<string[]>([]);

  async ngOnInit(): Promise<void> {
    try {
      const dates = await this.api.getUpcomingOccurrences(this.ruleId);
      this.dates.set(dates);
    } catch {
      // silence — dialog mostra "sem ocorrências"
    } finally {
      this.loading.set(false);
    }
  }
}
