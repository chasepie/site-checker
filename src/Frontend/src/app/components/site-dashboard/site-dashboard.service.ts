import { Injectable, signal } from '@angular/core';

@Injectable({
  providedIn: 'root'
})
export class SiteDashboardService {
  public readonly toggleSidebar = signal(false);
}
