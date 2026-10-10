import { HttpErrorResponse, HttpHandlerFn, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AdminTokenService } from '../services/admin-token.service';

/** The generated API clients request relative URLs such as `api/Site`. */
const API_URL = /^\/?api\//i;

/**
 * Sends the admin token with API requests. When the server rejects a request for lacking it, asks
 * for the token and retries once; a token rejected again is forgotten, so the next attempt asks.
 */
export const adminTokenInterceptor: HttpInterceptorFn = (req, next) => {
  if (!API_URL.test(req.url)) {
    return next(req);
  }

  const adminToken = inject(AdminTokenService);
  return send(req, next, adminToken.token).pipe(
    catchError((error: unknown) => {
      if (!isUnauthorized(error)) {
        return throwError(() => error);
      }

      adminToken.clear();
      return from(adminToken.prompt()).pipe(
        switchMap(token => token === null
          ? throwError(() => error)
          : send(req, next, token).pipe(
            catchError((retryError: unknown) => {
              if (isUnauthorized(retryError)) {
                adminToken.clear();
              }
              return throwError(() => retryError);
            }))),
      );
    }),
  );
};

function send(req: HttpRequest<unknown>, next: HttpHandlerFn, token: string | null) {
  return next(token === null ? req : req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
}

function isUnauthorized(error: unknown) {
  return error instanceof HttpErrorResponse && error.status === 401;
}
