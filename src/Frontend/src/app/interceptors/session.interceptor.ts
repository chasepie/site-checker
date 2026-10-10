import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

/** The generated API clients request relative URLs such as `api/Site`. */
const API_URL = /^\/?api\//i;
const AUTH_URL = /^\/?api\/auth\//i;

/**
 * Sends the browser to the login page when the API answers 401, because the session ended. The
 * login endpoints are left alone: a wrong password is their own 401.
 */
export const sessionInterceptor: HttpInterceptorFn = (req, next) => {
  if (!API_URL.test(req.url) || AUTH_URL.test(req.url)) {
    return next(req);
  }

  const auth = inject(AuthService);
  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        auth.sessionEnded();
      }
      return throwError(() => error);
    }),
  );
};
