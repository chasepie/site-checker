import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Sends a browser that isn't logged in to the login page, remembering where it was going. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  return auth.loggedIn()
    || inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** Logged in already, or login is off (Development): skips the login page for where it leads. */
export const loginGuard: CanActivateFn = route => {
  const auth = inject(AuthService);
  if (!auth.loggedIn()) {
    return true;
  }
  auth.goToReturnUrl(route.queryParamMap.get('returnUrl'));
  return false;
};
