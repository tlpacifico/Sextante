import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ThemeService } from './core/theme.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<router-outlet></router-outlet>`,
})
export class AppComponent {
  // Phase 5.5 — a re-hidratação da sessão acontece em provideAppInitializer
  // (app.config.ts), antes de o AuthGuard decidir o redirect inicial.
  // O ThemeService precisa de ser instanciado cedo para aplicar o tema
  // antes do primeiro paint.
  private readonly theme = inject(ThemeService);

  constructor() {
    void this.theme;
  }
}
