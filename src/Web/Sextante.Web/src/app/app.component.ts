import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AuthService } from './auth/auth.service';
import { ThemeService } from './core/theme.service';

/**
 * Root component — apenas renderiza o <c>&lt;router-outlet /&gt;</c>.
 * Os shells (auth-shell / app-shell) são montados pelas rotas.
 *
 * <p>No bootstrap injecta <c>AuthService</c> e <c>ThemeService</c> para
 * arrancar o ciclo de re-hidratação (cookie httpOnly → access token) e
 * aplicar o tema antes do primeiro paint, evitando "flash of unstyled
 * content".</p>
 */
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
    // ThemeService injetado para o effect aplicar a classe imediatamente.
    void this.theme;
    // Re-hidratar a sessão a partir do cookie httpOnly. Se não houver
    // cookie ou o refresh falhar, fica deslogado e o guard manda para /login.
    void this.auth.loadProfile();
  }
}
