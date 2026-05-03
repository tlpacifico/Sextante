import { Component, input, output } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { ConfirmPopupModule } from 'primeng/confirmpopup';
import { ConfirmationService } from 'primeng/api';

@Component({
  selector: 'sxt-confirm-dialog',
  standalone: true,
  imports: [ButtonModule, ConfirmPopupModule],
  providers: [ConfirmationService],
  template: `
    <p-confirmPopup />
    <ng-content />
  `,
})
export class ConfirmDialogComponent {
  readonly header = input('Confirmar');
  readonly message = input('Tem a certeza?');
  readonly severity = input<'danger' | 'warning' | 'info'>('danger');
  readonly confirm = output<void>();

  constructor(private readonly confirmationService: ConfirmationService) {}

  show(event: Event): void {
    this.confirmationService.confirm({
      target: event.target as EventTarget,
      message: this.message(),
      header: this.header(),
      icon: this.severity() === 'danger' ? 'pi pi-exclamation-triangle' : 'pi pi-info-circle',
      rejectButtonProps: {
        label: 'Cancelar',
        severity: 'secondary',
      },
      acceptButtonProps: {
        label: 'Confirmar',
        severity: this.severity(),
      },
      accept: () => {
        this.confirm.emit();
      },
    });
  }
}
