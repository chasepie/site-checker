import { Routes } from '@angular/router';
import { SiteDashboard } from './components/site-dashboard/site-dashboard';
import { authGuard, loginGuard } from './services/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [loginGuard],
    loadComponent: () => import('./components/login/login').then(m => m.Login)
  },
  {
    path: '',
    canActivateChild: [authGuard],
    children: [
      {
        path: '',
        pathMatch: 'full',
        component: SiteDashboard
      },
      {
        path: 'vpn',
        loadComponent: () => import('./components/vpn/vpn').then(m => m.Vpn)
      },
      {
        path: 'history',
        loadComponent: () => import('./components/history/history').then(m => m.History)
      },
    ],
  },
];
