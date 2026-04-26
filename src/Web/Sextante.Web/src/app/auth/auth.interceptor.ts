import { HttpInterceptorFn, HttpRequest, HttpEvent, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Observable, catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService, SKIP_AUTH, WITH_REFRESH_COOKIE } from './auth.service';

/**
 * Anexa <c>Authorization: Bearer &lt;accessToken&gt;</c> a requests
 * <c>/api/...</c> que não estejam marcados <c>SKIP_AUTH</c>. Em 401
 * tenta um refresh (com proteção de "storm" via Promise in-flight em
 * <c>AuthService</c>) e re-tenta a request original uma vez. Se o
 * refresh falhar, propaga o 401 e força logout no service.
 *
 * <p>Requests com <c>WITH_REFRESH_COOKIE = true</c> recebem
 * <c>withCredentials: true</c> para que o browser envie o cookie
 * httpOnly do refresh token (login / refresh). O resto dos endpoints
 * **não** envia cookies — superfície de ataque mínima.</p>
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);

  let request = applyContextOptions(req);

  if (request.context.get(SKIP_AUTH) || !request.url.startsWith('/api/')) {
    return next(request);
  }

  const accessToken = auth.getAccessToken();
  if (accessToken) {
    request = request.clone({
      setHeaders: { Authorization: `Bearer ${accessToken}` },
    });
  }

  return next(request).pipe(
    catchError((err: unknown) => handle401(err, request, next, auth)),
  );
};

function applyContextOptions<T>(req: HttpRequest<T>): HttpRequest<T> {
  if (req.context.get(WITH_REFRESH_COOKIE) && !req.withCredentials) {
    return req.clone({ withCredentials: true });
  }
  return req;
}

function handle401<T>(
  err: unknown,
  original: HttpRequest<T>,
  next: (request: HttpRequest<T>) => Observable<HttpEvent<T>>,
  auth: AuthService,
): Observable<HttpEvent<T>> {
  if (!(err instanceof HttpErrorResponse) || err.status !== 401) {
    return throwError(() => err);
  }
  if (original.url.includes('/api/auth/refresh') || original.url.includes('/api/auth/login')) {
    return throwError(() => err);
  }

  return from(auth.refresh()).pipe(
    switchMap((newToken) => {
      const retry = original.clone({
        setHeaders: { Authorization: `Bearer ${newToken}` },
      });
      return next(retry);
    }),
    catchError((refreshErr: unknown) => {
      // Force-clear local state so guard kicks in on next navigation.
      void auth.logout();
      return throwError(() => refreshErr);
    }),
  );
}
