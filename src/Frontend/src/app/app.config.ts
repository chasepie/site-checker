import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { sessionInterceptor } from './interceptors/session.interceptor';
import { AuthService } from './services/auth.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    // Also sends X-XSRF-TOKEN from the XSRF-TOKEN cookie on writes, which the server requires.
    provideHttpClient(withInterceptors([sessionInterceptor])),
    provideAppInitializer(async () => {
      // Reads the session (and the antiforgery token) before any route guard runs.
      await inject(AuthService).initialize();
    }),
  ]
};
