import { ChangeDetectionStrategy, Component, computed, effect, ElementRef, inject, viewChild } from '@angular/core';
import { Offcanvas } from 'bootstrap';
import { SiteDetails } from '../site-details/site-details';
import { SiteList } from '../site-list/site-list';
import { SiteDashboardService } from './site-dashboard.service';

@Component({
  selector: 'app-site-dashboard',
  imports: [SiteList, SiteDetails],
  templateUrl: './site-dashboard.html',
  styleUrl: './site-dashboard.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SiteDashboard {
  private readonly siteDashboardService = inject(SiteDashboardService);

  private readonly _offCanvasElement = viewChild<ElementRef<Element>>('offcanvas');
  protected readonly offcanvas = computed(() => {
    const element = this._offCanvasElement();
    return element ? new Offcanvas(element.nativeElement) : undefined;
  });

  public constructor() {
    effect(() => {
      const oc = this.offcanvas();
      if (!oc) {
        return;
      }

      const toggle = this.siteDashboardService.toggleSidebar();
      if (toggle) {
        oc.show();
      } else {
        oc.hide();
      }
    });
  }
}
