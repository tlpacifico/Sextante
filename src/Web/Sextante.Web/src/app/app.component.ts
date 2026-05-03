import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AuthService } from './auth/auth.service';
import { ThemeService } from './core/theme.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<router-outlet></router-outlet>`,
})
export class AppComponent {
  private readonly auth = inject(AuthService);
  private readonly theme = inject(ThemeService);

  constructor() {
    void this.theme;
    // Phase 5.5 — re-hidrata a sessão a partir de localStorage
    // (access + refresh tokens). Fallback: cookie httpOnly se localStorage
    // vazio. Se ambos falharem, fica deslogado e o guard manda para /login.
    void this.auth.rehydrateFromStorage();
  }
}
