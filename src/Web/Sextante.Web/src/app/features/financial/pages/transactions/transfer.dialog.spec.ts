import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MessageService } from 'primeng/api';
import { TransferDialogComponent } from './transfer.dialog';

describe('TransferDialogComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TransferDialogComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        MessageService,
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  it('shows the received-amount field only for cross-currency accounts and submits the expected body', () => {
    const fixture = TestBed.createComponent(TransferDialogComponent);
    fixture.componentInstance.visible = true;
    fixture.componentInstance.accounts = [
      { id: 'a1', name: 'Conta EUR', currency: 'EUR' },
      { id: 'a2', name: 'Conta USD', currency: 'USD' },
    ];
    fixture.componentInstance.ngOnChanges();
    fixture.detectChanges();

    const component = fixture.componentInstance as unknown as {
      form: {
        controls: {
          fromAccountId: { setValue(v: string): void };
          toAccountId: { setValue(v: string): void };
          amountOut: { setValue(v: number): void };
          amountIn: { setValue(v: number): void };
          description: { setValue(v: string): void };
        };
      };
      crossCurrency(): boolean;
      submit(): Promise<void>;
    };

    // Mesma moeda por defeito (nenhuma conta escolhida ainda) — campo escondido.
    expect(component.crossCurrency()).toBe(false);

    component.form.controls.fromAccountId.setValue('a1');
    component.form.controls.toAccountId.setValue('a2');
    fixture.detectChanges();

    expect(component.crossCurrency()).toBe(true);
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Valor recebido');

    component.form.controls.amountOut.setValue(100);
    component.form.controls.amountIn.setValue(120);
    component.form.controls.description.setValue('teste');

    component.submit();

    const req = httpMock.expectOne('/api/financial/transfers');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(
      jasmine.objectContaining({
        fromAccountId: 'a1',
        toAccountId: 'a2',
        amountOut: 100,
        amountIn: 120,
        description: 'teste',
      }),
    );
    req.flush({
      transferId: 't1',
      outLeg: { id: 'l1', accountId: 'a1' },
      inLeg: { id: 'l2', accountId: 'a2' },
    });
  });
});
