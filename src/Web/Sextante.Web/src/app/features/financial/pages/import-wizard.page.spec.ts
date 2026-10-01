import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { ImportWizardPage } from './import-wizard.page';
import { PreviewRow, StatementSummary, UploadCsvResponse } from '../../../core/api/financial.types';

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

  describe('extratos XLSX / PDF / JSON (Phase 6.6)', () => {
    const STATEMENT: StatementSummary = {
      format: 'ActivoBankCardPdf',
      currency: 'EUR',
      periodStart: '2026-09-01',
      periodEnd: '2026-09-30',
      balanceBefore: -2409.17,
      balanceAfter: -2383.66,
      checks: [
        { name: 'Total de débitos = resumo', passed: true, expected: '1109,73', actual: '1109,73' },
        { name: 'Total de créditos = resumo', passed: true, expected: '1135,24', actual: '1135,24' },
      ],
      rowsTrimmed: 2,
      pendingIgnored: 1,
      cancelledIgnored: 0,
    };

    function statementResponse(rows: PreviewRow[], statement: StatementSummary = STATEMENT): UploadCsvResponse {
      return { ...response(rows), headers: ['Data Lanc.', 'Data Valor', 'Descrição', 'Valor', 'Saldo'], statement };
    }

    it('accepts xlsx, pdf and json files and hides the CSV profile selector for them', async () => {
      const fixture = await create();
      const el = fixture.nativeElement as HTMLElement;
      const page = fixture.componentInstance as any;
      expect(text(el)).toContain('Perfil de importação');

      for (const name of ['mov.xlsx', 'extrato.PDF', 'coverflex.json']) {
        page.setFile(new File(['x'], name));
        fixture.detectChanges();
        expect(page.selectedFile()?.name).toBe(name);
        expect(text(el)).not.toContain('Perfil de importação');
      }

      page.setFile(new File(['x'], 'extrato.csv'));
      fixture.detectChanges();
      expect(text(el)).toContain('Perfil de importação');
    });

    it('rejects other extensions', async () => {
      const fixture = await create();
      const page = fixture.componentInstance as any;

      page.setFile(new File(['x'], 'extrato.txt'));

      expect(page.selectedFile()).toBeNull();
    });

    it('skips the mapping step and shows the validation card after uploading a statement', async () => {
      const fixture = await create();
      const page = fixture.componentInstance as any;
      page.selectedFile.set(new File(['x'], 'extrato.pdf'));
      page.uploadForm.controls.accountId.setValue('a2');

      const done = page.analyzeFile();
      const req = httpMock.expectOne('/api/financial/imports/upload?accountId=a2');
      expect(req.request.method).toBe('POST');
      req.flush(statementResponse([row({ values: ['08/09/2026', '09/09/2026', 'COMPRA', '-22,14', ''] })]));
      await done;
      fixture.detectChanges();

      expect(page.activeStep()).toBe(3);
      const el = fixture.nativeElement as HTMLElement;
      const card = el.querySelector('[data-testid="statement-validation-card"]');
      expect(card).not.toBeNull();
      const content = text(card as HTMLElement);
      expect(content).toContain('Cartão de crédito ActivoBank (PDF)');
      expect(content).toContain('01/09/2026 a 30/09/2026');
      expect(content).toContain('Total de débitos = resumo');
      expect(content).toContain('esperado 1109,73 · calculado 1109,73');
      expect(content).toContain('2 linhas cortadas por já estarem importadas');
      expect(content).toContain('1 movimento pendente não importado');
      expect(text(el)).not.toContain('Mapeamento de colunas');
      expect(page.steps().map((s: { label: string }) => s.label)).toEqual(['Ficheiro', 'Confirmação', 'Resultado']);
    });

    it('does not send the selected CSV profile when uploading a statement', async () => {
      const fixture = await create();
      const page = fixture.componentInstance as any;
      page.uploadForm.controls.importProfileId.setValue('p1');
      page.selectedFile.set(new File(['x'], 'mov.xlsx'));
      page.uploadForm.controls.accountId.setValue('a1');

      const done = page.analyzeFile();
      httpMock.expectOne('/api/financial/imports/upload?accountId=a1').flush(statementResponse([row({})]));
      await done;
    });

    it('shows the backend validation message in evidence, with no preview, when the upload is rejected', async () => {
      const fixture = await create();
      const page = fixture.componentInstance as any;
      page.selectedFile.set(new File(['x'], 'mov.xlsx'));
      page.uploadForm.controls.accountId.setValue('a1');

      const done = page.analyzeFile();
      httpMock.expectOne('/api/financial/imports/upload?accountId=a1').flush(
        { errors: { file: ['O saldo não encadeia no movimento 4 (07/09/2026): esperado 1352,32, encontrado 1352,99.'] } },
        { status: 400, statusText: 'Bad Request' });
      await done;
      fixture.detectChanges();

      const alert = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="upload-error"]');
      expect(alert?.getAttribute('role')).toBe('alert');
      expect(text(alert as HTMLElement)).toContain('esperado 1352,32, encontrado 1352,99');
      expect(page.activeStep()).toBe(1);
      expect(page.uploadResponse()).toBeNull();
    });

    it('clears the error when another file is chosen', async () => {
      const fixture = await create();
      const page = fixture.componentInstance as any;
      page.uploadError.set('Erro anterior');

      page.setFile(new File(['x'], 'outro.xlsx'));

      expect(page.uploadError()).toBeNull();
    });

    it('disables the confirmation when every row was already imported', async () => {
      const fixture = await create();
      const page = fixture.componentInstance as any;
      page.uploadResponse.set(statementResponse([], { ...STATEMENT, rowsTrimmed: 6 }));
      page.activeStep.set(3);
      fixture.detectChanges();

      const el = fixture.nativeElement as HTMLElement;
      expect(el.querySelector('[data-testid="nothing-new"]')).not.toBeNull();
      const confirm = Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes('Confirmar importação'));
      expect(confirm?.disabled).toBeTrue();
    });

    it('goes back to the file step (not to mapping) from the statement confirmation', async () => {
      const fixture = await create();
      const page = fixture.componentInstance as any;
      page.uploadResponse.set(statementResponse([row({})]));
      page.activeStep.set(3);
      fixture.detectChanges();

      const back = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'))
        .find((b) => b.textContent?.includes('Voltar'));
      back?.click();

      expect(page.activeStep()).toBe(1);
    });

    it('keeps the CSV flow with the mapping step', async () => {
      const fixture = await create();
      const page = fixture.componentInstance as any;
      page.selectedFile.set(new File(['x'], 'extrato.csv'));
      page.uploadForm.controls.accountId.setValue('a1');

      const done = page.analyzeFile();
      httpMock.expectOne('/api/financial/imports/upload?accountId=a1').flush(response([row({})]));
      await done;
      fixture.detectChanges();

      expect(page.activeStep()).toBe(2);
      expect(page.steps().length).toBe(4);
      expect(text(fixture.nativeElement)).toContain('Mapeamento de colunas');
    });
  });
});
