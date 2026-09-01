import { TestBed } from '@angular/core/testing';
import { MessageService } from 'primeng/api';
import { AuthService } from '../auth/auth.service';
import { EmailConfirmationBannerComponent } from './email-confirmation-banner.component';

describe('EmailConfirmationBannerComponent', () => {
  let resend: jasmine.Spy;

  function setup(options: { authenticated: boolean; confirmed: boolean }) {
    resend = jasmine.createSpy('resendConfirmationEmail').and.resolveTo(undefined);

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [EmailConfirmationBannerComponent],
      providers: [
        MessageService,
        {
          provide: AuthService,
          useValue: {
            isAuthenticated: () => options.authenticated,
            emailConfirmed: () => options.confirmed,
            resendConfirmationEmail: resend,
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(EmailConfirmationBannerComponent);
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => sessionStorage.clear());

  it('shows the banner when the email is not confirmed', () => {
    const fixture = setup({ authenticated: true, confirmed: false });
    const banner = fixture.nativeElement.querySelector('[data-testid="email-confirmation-banner"]');
    expect(banner).toBeTruthy();
    expect(banner.textContent).toContain('Confirme');
  });

  it('stays hidden when the email is already confirmed', () => {
    const fixture = setup({ authenticated: true, confirmed: true });
    expect(
      fixture.nativeElement.querySelector('[data-testid="email-confirmation-banner"]'),
    ).toBeNull();
  });

  it('stays hidden when nobody is authenticated', () => {
    const fixture = setup({ authenticated: false, confirmed: false });
    expect(
      fixture.nativeElement.querySelector('[data-testid="email-confirmation-banner"]'),
    ).toBeNull();
  });

  it('asks the auth service to resend the confirmation email', async () => {
    const fixture = setup({ authenticated: true, confirmed: false });
    const button: HTMLButtonElement = fixture.nativeElement.querySelector(
      '[data-testid="email-confirmation-resend"] button',
    );

    button.click();
    await fixture.whenStable();

    expect(resend).toHaveBeenCalled();
  });

  it('dismissing hides the banner for the rest of the session', async () => {
    const fixture = setup({ authenticated: true, confirmed: false });
    const button: HTMLButtonElement = fixture.nativeElement.querySelector(
      '[data-testid="email-confirmation-dismiss"] button',
    );

    button.click();
    fixture.detectChanges();

    expect(
      fixture.nativeElement.querySelector('[data-testid="email-confirmation-banner"]'),
    ).toBeNull();

    const second = setup({ authenticated: true, confirmed: false });
    expect(
      second.nativeElement.querySelector('[data-testid="email-confirmation-banner"]'),
    ).toBeNull();
  });
});
