import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { ImportWizardPage } from './import-wizard.page';
import { PreviewRow, UploadCsvResponse } from '../../../core/api/financial.types';

const ACCOUNTS = [
  {
    id: 'a1', name: 'Conta à ordem', type: 'Checking', currency: 'EUR',
    openingBalance: { amount: 0, currency: 'EUR' }, openingBalanceDate: '2026-08-28',
    currentBalance: { amount: 0, currency: 'EUR' }, createdAt: '', updatedAt: '', creditCard: null,
  },
  {
    id: 'a2', name: 'Cartão', type: 'CreditCard', currency: 'EUR',
    openingBalance: { amount: -500, currency: 'EUR' }, openingBalanceDate: '2026-08-01',
    currentBalance: { amount: -500, currency: 'EUR' }, createdAt: '', updatedAt: '', creditCard: null,
  },
];

function row(overrides: Partial<PreviewRow>): PreviewRow {
  return {
    rowIndex: 0,
    values: ['11/09/2026', 'VIS PAGAMENTO CARTAO', '-450,00'],
    isDuplicate: false,
    duplicateTransactionId: null,
    suggestedCategoryName: null,
    suggestedCategoryId: null,
    isAutoCategorized: false,
    error: null,
    accountId: 'a1',
    isBeforeOpeningBalance: false,
    transferStatus: null,
    transferTargetAccountId: null,
    transferTargetAccountName: null,
    transferCounterpartTransactionId: null,
    ...overrides,
  };
}

function response(rows: PreviewRow[]): UploadCsvResponse {
  return {
    batchId: 'b1',
    headers: ['Data', 'Descrição', 'Valor'],
    previewRows: rows,
    totalRowCount: rows.length,
    truncated: false,
    detectedDelimiter: ';',
    detectedHasHeader: true,
    errors: [],
  };
}

describe('ImportWizardPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ImportWizardPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        provideRouter([]),
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  async function create(accounts: unknown[] = ACCOUNTS) {
    const fixture = TestBed.createComponent(ImportWizardPage);
    fixture.detectChanges();
    httpMock.expectOne('/api/financial/import-profiles').flush([]);
    httpMock.expectOne('/api/financial/accounts').flush(accounts);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  function text(el: HTMLElement): string {
    return el.textContent ?? '';
  }

  it('loads profiles and accounts, and needs an account before analysing', async () => {
    const fixture = await create();
    const el = fixture.nativeElement as HTMLElement;

    expect(text(el)).toContain('Conta de destino');
    const page = fixture.componentInstance as any;
    page.selectedFile.set(new File(['x'], 'extrato.csv', { type: 'text/csv' }));
    fixture.detectChanges();

    const analyse = Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes('Analisar ficheiro'));
    expect(analyse?.disabled).toBeTrue();

    page.uploadForm.controls.accountId.setValue('a1');
    fixture.detectChanges();
    expect(analyse?.disabled).toBeFalse();
  });

  it('preselects the only account', async () => {
    const fixture = await create([ACCOUNTS[0]]);

    expect((fixture.componentInstance as any).uploadForm.controls.accountId.value).toBe('a1');
  });

  it('shows the transfer status of a row in the preview', async () => {
    const fixture = await create();
    const page = fixture.componentInstance as any;
    page.uploadResponse.set(response([
      row({ transferStatus: 'AlreadyRecorded', transferTargetAccountName: 'Cartão', isDuplicate: true }),
    ]));
    page.activeStep.set(3);
    fixture.detectChanges();

    const content = text(fixture.nativeElement);
    expect(content).toContain('Transferência → Cartão');
    expect(content).toContain('já registada');
  });

  it('flags rows before the opening balance and sends the toggle on confirm', async () => {
    const fixture = await create();
    const page = fixture.componentInstance as any;
    page.uploadResponse.set(response([row({ isBeforeOpeningBalance: true })]));
    page.activeStep.set(3);
    fixture.detectChanges();

    const content = text(fixture.nativeElement);
    expect(content).toContain('Antes do saldo inicial');
    expect(content).toContain('Incluir 1 linha anterior ao saldo inicial');

    page.includeBeforeOpeningBalance.set(true);
    const done = page.confirmImport();
    const req = httpMock.expectOne('/api/financial/imports/b1/confirm');
    expect(req.request.body).toEqual({ includeDuplicates: [], includeBeforeOpeningBalance: true });
    req.flush({
      batchId: 'b1', importedRows: 1, autoCategorized: 0, manualCount: 1, errorRows: 0, status: 'Completed',
      skippedBeforeOpeningBalance: 0, transfersCreated: 0, transfersLinked: 0, transfersAlreadyRecorded: 0,
    });
    await done;
  });
});
