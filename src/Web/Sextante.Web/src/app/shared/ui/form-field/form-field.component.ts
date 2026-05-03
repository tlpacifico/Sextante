import { Component, input } from '@angular/core';
import { AbstractControl } from '@angular/forms';
import { NgClass } from '@angular/common';

type ErrorMessages = Record<string, string>;

const DEFAULT_MESSAGES: ErrorMessages = {
  required: 'Campo obrigatório.',
  email: 'Email inválido.',
  minlength: 'Valor demasiado curto.',
  maxlength: 'Valor demasiado longo.',
  min: 'Valor abaixo do mínimo permitido.',
  max: 'Valor acima do máximo permitido.',
  pattern: 'Formato inválido.',
};

@Component({
  selector: 'sxt-form-field',
  standalone: true,
  imports: [NgClass],
  template: `
    <div class="flex flex-col gap-1">
      @if (label()) {
        <label class="text-sm font-medium text-neutral-700 dark:text-neutral-300">
          {{ label() }}
          @if (required()) {
            <span class="text-danger">*</span>
          }
        </label>
      }
      <ng-content />
      @if (control()?.invalid && (control()?.dirty || control()?.touched)) {
        <small class="text-xs text-danger mt-0.5">
          {{ getErrorMessage() }}
        </small>
      }
    </div>
  `,
})
export class FormFieldComponent {
  readonly label = input<string>();
  readonly control = input<AbstractControl | null>();
  readonly required = input(false);
  readonly errorMessages = input<ErrorMessages>({});

  getErrorMessage(): string {
    const ctrl = this.control();
    if (!ctrl?.errors) return '';

    const messages = { ...DEFAULT_MESSAGES, ...this.errorMessages() };
    const firstErrorKey = Object.keys(ctrl.errors)[0];
    return messages[firstErrorKey] ?? `Erro: ${firstErrorKey}`;
  }
}
