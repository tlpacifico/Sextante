import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Permite a navegação se houver sessão; senão devolve um <c>UrlTree</c>
 * para <c>/login?returnUrl=&lt;path&gt;</c>. Usar como <c>canActivate</c>
 * nas rotas autenticadas.
 *
 * <p>O bootstrap da app (<c>main.ts</c> / shell) é responsável por
 * chamar <c>AuthService.loadProfile()</c> uma vez antes da primeira
 * navegação para que o cookie httpOnly tenha hipótese de re-hidratar
 * o estado. Sem isso, F5 numa rota guardada manda sempre para o login.</p>
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/login'], {
    queryParams: { returnUrl: state.url },
  });
};
