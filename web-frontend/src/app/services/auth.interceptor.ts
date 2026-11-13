import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

const isAuthCall = (url: string) => /\/api\/auth\/(login|refresh|register)/.test(url);

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const token = auth.accessToken();
  const authed =
    token && !isAuthCall(req.url)
      ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
      : req;
  return next(authed).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse && err.status === 401 && !isAuthCall(req.url)) {
        if (!auth.refreshToken()) {
          auth.logout();
          return throwError(() => err);
        }
        return from(auth.refresh()).pipe(
          switchMap((ok) => {
            if (!ok) {
              auth.logout();
              return throwError(() => err);
            }
            return next(
              req.clone({ setHeaders: { Authorization: `Bearer ${auth.accessToken()}` } }),
            );
          }),
        );
      }
      return throwError(() => err);
    }),
  );
};
