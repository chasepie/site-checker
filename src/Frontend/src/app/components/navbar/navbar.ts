import { ChangeDetectionStrategy, Component, inject, signal, TemplateRef } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgbOffcanvas } from '@ng-bootstrap/ng-bootstrap';
import { AuthService } from '../../services/auth.service';
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
  protected readonly auth = inject(AuthService);
  protected readonly logoutFailed = signal(false);

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

  protected async logout() {
    this.logoutFailed.set(false);
    try {
      await this.auth.logout();
    } catch {
      this.logoutFailed.set(true);
    }
  }
}
