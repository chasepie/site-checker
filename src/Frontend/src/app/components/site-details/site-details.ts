import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgbDropdownModule, NgbModal, NgbPaginationModule } from '@ng-bootstrap/ng-bootstrap';
import { SiteCheck, SiteCheckController } from '../../generated/model';
import { SiteCheckStore } from '../../services/site-check.store';
import { SiteStore } from '../../services/site.store';
import { convertTimeOnlyStringToDate } from '../../utilities/type-utils';
import { EditSite } from '../edit-site/edit-site';
import { SiteCheckDetails } from '../site-check-details/site-check-details';

const MAX_URL_LENGTH = 200;

@Component({
  selector: 'app-site-details',
  templateUrl: './site-details.html',
  styleUrl: './site-details.scss',
  imports: [FormsModule, NgbPaginationModule, DatePipe, NgbDropdownModule],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SiteDetails {
  private readonly _siteStore = inject(SiteStore);
  private readonly _siteCheckStore = inject(SiteCheckStore);
  private readonly _controller = inject(SiteCheckController);
  private readonly _modalService = inject(NgbModal);

  protected readonly site = this._siteStore.selectedSite;
  protected readonly siteChecks = this._siteCheckStore.filteredEntities;
  protected readonly pageNumber = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly totalItemCount = this._siteCheckStore.totalItemCount;
  protected readonly optionsPageSize = [5, 10, 25, 50, 100];

  public constructor() {
    effect(() => {
      this._siteCheckStore.loadSiteChecks({
        siteId: this.site()?.id,
        pageNumber: this.pageNumber() - 1,
        pageSize: this.pageSize()
      });
    });
  }

  protected readonly truncatedUrl = computed(() => {
    const url = this.site()?.url ?? '';
    if (url.length > MAX_URL_LENGTH) {
      return url.slice(0, MAX_URL_LENGTH - 3) + '...';
    }
    return url;
  });

  protected readonly scheduleText = computed(() => {
    const site = this.site();
    if (!site) {
      return 'N/A';
    }

    const schedule = site.schedule;
    if (!schedule.enabled || !schedule.start || !schedule.end || !schedule.interval) {
      return 'None';
    }

    const startTime = convertTimeOnlyStringToDate(schedule.start)
      .toLocaleTimeString()
      .replace(/:\d{2} /, ' ');
    const endTime = convertTimeOnlyStringToDate(schedule.end)
      .toLocaleTimeString()
      .replace(/:\d{2} /, ' ');

    const interval = schedule.interval === 1
      ? 'minute'
      : `${schedule.interval} minutes`;
    return `Every ${interval}, ${startTime} to ${endTime}`;
  });

  protected readonly optionsText = computed(() => {
    const site = this.site();
    if (!site) {
      return 'N/A';
    }

    const options: string[] = [];
    if (site.useVpn) {
      options.push('Uses VPN');
    }
    if (site.alwaysTakeScreenshot) {
      options.push('Always takes screenshots');
    }
    return options.join(', ') || 'None';
  });

  protected async queueCheck() {
    const site = this.site();
    if (!site) {
      throw new Error('No site selected');
    }

    await this._controller.createSiteCheck(site.id);
  }

  protected async deleteCheck(siteCheck: SiteCheck) {
    await this._siteCheckStore.deleteSiteCheck(siteCheck);
  }

  protected openCheckDetails(siteCheck: SiteCheck) {
    const modalRef = this._modalService.open(SiteCheckDetails, {
      size: 'lg',
      fullscreen: 'sm',
      scrollable: true,
    });
    (modalRef.componentInstance as SiteCheckDetails).siteCheckId.set(siteCheck.id);
  }

  protected async deleteAllChecks() {
    const site = this.site();
    if (!site) {
      throw new Error('No site selected');
    }
    await this._siteCheckStore.deleteAllSiteChecks(site.id);
  }

  protected async createEmptyCheck() {
    const site = this.site();
    if (!site) {
      throw new Error('No site selected');
    }
    await this._siteCheckStore.createEmptyCheck(site.id);
  }

  protected editSite() {
    const s = this.site();
    if (!s) {
      return;
    }

    this._modalService.open(EditSite, {
      size: 'lg',
      fullscreen: 'sm',
      scrollable: true,
    });
  }
}
