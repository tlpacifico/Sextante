import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { provideRouter } from '@angular/router';
import { CanActivateFn, ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { AuthService } from './auth.service';
import { authGuard } from './auth.guard';

class FakeAuthService {
  private authenticated = false;
  setAuthenticated(value: boolean) {
    this.authenticated = value;
  }
  isAuthenticated() {
    return this.authenticated;
  }
}

describe('authGuard', () => {
  let auth: FakeAuthService;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useClass: FakeAuthService },
      ],
    });
    auth = TestBed.inject(AuthService) as unknown as FakeAuthService;
    router = TestBed.inject(Router);
  });

  it('returns true when the user is authenticated', () => {
    auth.setAuthenticated(true);
    const result = runGuard('/app/dashboard');
    expect(result).toBeTrue();
  });

  it('returns a UrlTree to /login with returnUrl when not authenticated', () => {
    auth.setAuthenticated(false);
    const result = runGuard('/app/dashboard') as UrlTree;
    expect(result instanceof UrlTree).toBeTrue();

    const expected = router.createUrlTree(['/login'], {
      queryParams: { returnUrl: '/app/dashboard' },
    });
    expect(result.toString()).toBe(expected.toString());
  });
});

function runGuard(url: string): boolean | UrlTree {
  const guard = authGuard as CanActivateFn;
  const state = { url } as RouterStateSnapshot;
  return TestBed.runInInjectionContext(() =>
    guard({} as ActivatedRouteSnapshot, state),
  ) as boolean | UrlTree;
}
