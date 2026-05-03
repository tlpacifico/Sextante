import { Component, input } from '@angular/core';

@Component({
  selector: 'sxt-page-header',
  standalone: true,
  imports: [],
  template: `
    <div class="mb-6">
      <div class="flex flex-col gap-1">
        <h1 class="text-xl md:text-2xl font-semibold text-neutral-900 dark:text-neutral-100">
          {{ title() }}
        </h1>
        @if (description()) {
          <p class="text-sm text-neutral-500 dark:text-neutral-400">
            {{ description() }}
          </p>
        }
      </div>
      @if (actionsSlot()) {
        <div class="mt-4 flex items-center gap-2">
          <ng-content select="[actions]" />
        </div>
      }
    </div>
  `,
})
export class PageHeaderComponent {
  readonly title = input.required<string>();
  readonly description = input<string>();
  readonly actionsSlot = input(false);
}
