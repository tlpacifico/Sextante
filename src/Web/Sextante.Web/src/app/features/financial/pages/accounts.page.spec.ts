import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MessageService } from 'primeng/api';
import { AccountsPage } from './accounts.page';

describe('AccountsPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AccountsPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  it('explains why an account with active transactions cannot be archived', async () => {
    const fixture = TestBed.createComponent(AccountsPage);
    fixture.detectChanges();
    // Pedidos de arranque (contas, moedas, definições do tenant) — irrelevantes aqui.
    httpMock.match(() => true).forEach((req) => req.flush([]));

    const toast = fixture.debugElement.injector.get(MessageService);
    const add = spyOn(toast, 'add');

    const archiving = (fixture.componentInstance as unknown as {
      archive(id: string): Promise<void>;
    }).archive('acc-1');

    httpMock
      .expectOne('/api/financial/accounts/acc-1')
      .flush(
        { title: 'Erros de validação', errors: { account: ['x'] } },
        { status: 400, statusText: 'Bad Request' },
      );
    await archiving;

    expect(add).toHaveBeenCalledWith(
      jasmine.objectContaining({
        severity: 'error',
        detail: 'Não é possível arquivar uma conta com transações ativas.',
      }),
    );
  });
});
