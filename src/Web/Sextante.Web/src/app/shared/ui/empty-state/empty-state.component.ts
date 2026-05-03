import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';

@Component({
  selector: 'sxt-empty-state',
  standalone: true,
  imports: [RouterLink, ButtonModule],
  template: `
    <div class="flex flex-col items-center justify-center py-12 text-center">
      <i [class]="icon()" class="text-5xl text-neutral-300 dark:text-neutral-600 mb-4"></i>
      <h3 class="text-lg font-medium text-neutral-700 dark:text-neutral-300">
        {{ title() }}
      </h3>
      @if (description()) {
        <p class="mt-1 text-sm text-neutral-500 dark:text-neutral-400 max-w-sm">
          {{ description() }}
        </p>
      }
      @if (ctaLabel() && ctaRoute()) {
        <a [routerLink]="ctaRoute()"
           class="mt-4 inline-flex items-center gap-2 px-4 py-2 rounded-lg
                  bg-primary-500 text-white text-sm font-medium
                  hover:bg-primary-600 transition-colors">
          <i class="pi pi-plus text-xs"></i>
          {{ ctaLabel() }}
        </a>
      }
    </div>
  `,
})
export class EmptyStateComponent {
  readonly icon = input.required<string>();
  readonly title = input.required<string>();
  readonly description = input<string>();
  readonly ctaLabel = input<string>();
  readonly ctaRoute = input<string>();
}
