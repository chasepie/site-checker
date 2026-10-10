import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, model } from '@angular/core';
import { NgbActiveModal } from '@ng-bootstrap/ng-bootstrap';
import { ScreenshotStore } from '../../services/screenshot.store';
import { SiteCheckStore } from '../../services/site-check.store';
import { VpnStore } from '../../services/vpn.store';
import { requestedActionLabel, vpnLocationLabel } from '../../utilities/labels';

@Component({
  selector: 'app-site-check-details',
  imports: [DatePipe],
  templateUrl: './site-check-details.html',
  styleUrl: './site-check-details.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SiteCheckDetails {
  private readonly _siteCheckStore = inject(SiteCheckStore);
  private readonly _screenshotStore = inject(ScreenshotStore);
  private readonly _vpnStore = inject(VpnStore);
  protected readonly activeModal = inject(NgbActiveModal);
  protected readonly requestedActionLabel = requestedActionLabel;
  public readonly siteCheckId = model<number>();

  protected readonly siteCheck = computed(() => {
    return this._siteCheckStore.entities()
      .find(sc => sc.id === this.siteCheckId());
  });

  protected readonly vpnLocation = computed(() => {
    const sc = this.siteCheck();
    return sc ? vpnLocationLabel(sc.vpnLocationId, this._vpnStore.entityMap()) : '';
  });

  protected readonly screenshot = computed(() => {
    const sc = this.siteCheck();
    if (!sc) {
      return null;
    }
    const data = this._screenshotStore.entities()
      .find(s => s.siteCheckId === sc.id)?.data;

    return data ? `data:image/png;base64,${data}` : null;
  });

  public constructor() {
    effect(() => {
      const sc = this.siteCheck();
      if (sc) {
        this._screenshotStore.loadScreenshot(sc);
      }
    });
  }

  protected async deleteSiteCheck() {
    const sc = this.siteCheck();
    if (!sc) {
      return;
    }
    this.activeModal.close('Deleted');
    await this._siteCheckStore.deleteSiteCheck(sc);
  }
}
