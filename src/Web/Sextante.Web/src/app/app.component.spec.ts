import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AppComponent } from './app.component';
import { AuthService } from './auth/auth.service';

class FakeAuthService {
  isAuthenticated() {
    return false;
  }
  loadProfile() {
    return Promise.resolve(false);
  }
  rehydrateFromStorage() {
    return Promise.resolve(false);
  }
  getAccessToken() {
    return null;
  }
  userEmail() {
    return null;
  }
  tenantName() {
    return null;
  }
  tenantRole() {
    return null;
  }
}

describe('AppComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useClass: FakeAuthService },
      ],
    }).compileComponents();
  });

  it('should create the root component', () => {
    const fixture = TestBed.createComponent(AppComponent);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render an outlet for the active shell', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('router-outlet')).not.toBeNull();
  });
});
