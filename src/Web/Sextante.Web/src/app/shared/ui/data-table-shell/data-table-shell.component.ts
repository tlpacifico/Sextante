import { Component, ContentChild, TemplateRef, input } from '@angular/core';
import { TableModule } from 'primeng/table';
import { SkeletonModule } from 'primeng/skeleton';

@Component({
  selector: 'sxt-data-table-shell',
  standalone: true,
  imports: [TableModule, SkeletonModule],
  template: `
    <div class="overflow-x-auto">
      @if (loading()) {
        <div class="space-y-2">
          @for (i of [1, 2, 3, 4, 5]; track i) {
            <p-skeleton height="3rem" />
          }
        </div>
      } @else if (empty()) {
        <sxt-empty-state
          [icon]="emptyIcon()"
          [title]="emptyTitle()"
          [description]="emptyDescription()" />
      } @else {
        <p-table
          [value]="items()"
          [paginator]="paginator()"
          [rows]="rows()"
          [totalRecords]="totalRecords()"
          [rowsPerPageOptions]="rowsPerPageOptions()"
          [lazy]="lazy()"
          [scrollable]="true"
          [scrollHeight]="scrollable() ? 'flex' : undefined"
          [styleClass]="'text-sm'"
          (onLazyLoad)="onLazyLoad.emit($event)"
          [tableStyle]="{ 'min-width': '50rem' }">
          <ng-content select="[columns]" />
        </p-table>
      }
    </div>
  `,
})
export class DataTableShellComponent {
  readonly items = input<any[]>([]);
  readonly loading = input(false);
  readonly empty = input(false);
  readonly emptyIcon = input('pi pi-inbox');
  readonly emptyTitle = input('Sem dados');
  readonly emptyDescription = input<string>();
  readonly paginator = input(true);
  readonly rows = input(20);
  readonly totalRecords = input(0);
  readonly rowsPerPageOptions = input<number[]>([10, 20, 50, 100]);
  readonly lazy = input(false);
  readonly scrollable = input(true);
  readonly onLazyLoad = output<any>();
}
