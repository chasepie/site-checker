import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Sends a browser that isn't logged in to the login page, remembering where it was going. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  return auth.loggedIn()
    || inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};
