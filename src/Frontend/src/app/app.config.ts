import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { adminTokenInterceptor } from './interceptors/admin-token.interceptor';
import { SignalrService } from './services/signalr.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([adminTokenInterceptor])),
    provideAppInitializer(async () => {
      const signalrService = inject(SignalrService);
      await signalrService.init();
    }),
  ]
};
