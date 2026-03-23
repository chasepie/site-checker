import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { CheckStatus, Site } from '../../generated/model';
import { SiteStore } from '../../services/site.store';
import { SiteDashboardService } from '../site-dashboard/site-dashboard.service';

@Component({
  selector: 'app-site-list',
  templateUrl: './site-list.html',
  styleUrl: './site-list.scss',
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SiteList {
  private readonly _siteDashboardService = inject(SiteDashboardService);
  private readonly _siteStore = inject(SiteStore);
  protected sites = this._siteStore.sitesWithLatestCheck;
  protected selectedSite = this._siteStore.selectedSite;
  protected readonly CheckStatus = CheckStatus;

  protected selectSite(site: Site): void {
    this._siteStore.selectSite(site);
    this._siteDashboardService.toggleSidebar.set(false);
  }

  public constructor() {
    effect(() => {
      const allSites = this.sites();
      if (allSites.length > 0 && !this.selectedSite()) {
        this.selectSite(allSites[0]);
      }
    });
  }
}
