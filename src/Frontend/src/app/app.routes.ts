import { Routes } from '@angular/router';
import { SiteDashboard } from './components/site-dashboard/site-dashboard';

export const routes: Routes = [
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
];
