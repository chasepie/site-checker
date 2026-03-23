import { ChangeDetectionStrategy, Component, inject, TemplateRef } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgbOffcanvas } from '@ng-bootstrap/ng-bootstrap';
import { SiteDashboardService } from '../site-dashboard/site-dashboard.service';

@Component({
  selector: 'app-navbar',
  imports: [RouterLink],
  templateUrl: './navbar.html',
  styleUrl: './navbar.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Navbar {
  private readonly _siteDashboardService = inject(SiteDashboardService);
  private _offcanvasService = inject(NgbOffcanvas);

  protected readonly pages = [
    { name: 'Dashboard', route: '/' },
    { name: 'VPN', route: '/vpn' },
    { name: 'History', route: '/history' },
  ] as const;

  openSidebar(content: TemplateRef<unknown>) {
    this._offcanvasService.open(content, { position: 'end' });
  }

  openSiteList() {
    this._siteDashboardService.toggleSidebar.set(true);
  }
}
