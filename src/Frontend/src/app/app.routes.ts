import { Routes } from '@angular/router';
import { SiteDashboard } from './components/site-dashboard/site-dashboard';
import { authGuard } from './services/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
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
        path: 'sites/new',
        loadComponent: () => import('./components/site-editor/site-editor').then(m => m.SiteEditor)
      },
      {
        path: 'sites/:id/edit',
        loadComponent: () => import('./components/site-editor/site-editor').then(m => m.SiteEditor)
      },
      {
        path: 'history',
        loadComponent: () => import('./components/history/history').then(m => m.History)
      },
    ],
  },
];
