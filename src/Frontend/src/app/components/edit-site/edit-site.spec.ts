import { TestBed } from '@angular/core/testing';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { NgbActiveModal } from '@ng-bootstrap/ng-bootstrap';
import { Site, SiteCheckController, SiteController } from '../../generated/model';
import { SignalrService } from '../../services/signalr.service';
import { SiteStore } from '../../services/site.store';
import { EditSite } from './edit-site';

const site: Site = {
  id: 1,
  name: 'Saved name',
  url: 'https://example.com',
  scraperId: 'example',
  useVpn: false,
  alwaysTakeScreenshot: false,
  knownFailuresThreshold: 3,
  schedule: { enabled: false, start: null, end: null, interval: null },
  pushoverConfig: { successPriority: null, failurePriority: null },
  discordConfig: { successEnabled: false, failureEnabled: false, channelId: null },
  siteChecks: [],
};

describe('EditSite', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('keeps unsaved changes when the hub reconnects and the Sites load again', async () => {
    // A hub that connects at once.
    const connection = {
      state: HubConnectionState.Disconnected,
      start: vi.fn().mockResolvedValue(undefined),
      onreconnected: vi.fn(),
      on: vi.fn(),
      off: vi.fn(),
    };
    vi.spyOn(HubConnectionBuilder.prototype, 'build').mockReturnValue(connection as unknown as HubConnection);
    // Each load returns a new copy of the same Site, as the server does.
    const getAllSites = vi.fn(() => Promise.resolve([structuredClone(site)]));
    const updateSite = vi.fn((_id: number, update: Site) => Promise.resolve(update));
    TestBed.configureTestingModule({
      providers: [
        { provide: SiteController, useValue: { getAllSites, updateSite } },
        { provide: SiteCheckController, useValue: {} },
        { provide: NgbActiveModal, useValue: { close: vi.fn() } },
      ],
    });
    const store = TestBed.inject(SiteStore);
    await vi.waitFor(() => { expect(store.entities()).toHaveLength(1); });
    store.selectSite(store.entities()[0]);
    const loaded = store.selectedSite();
    const fixture = TestBed.createComponent(EditSite);
    await fixture.whenStable();
    const name = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('#name');
    if (!name) {
      throw new Error('The name field is missing.');
    }
    name.value = 'Unsaved name';
    name.dispatchEvent(new Event('input'));

    await TestBed.inject(SignalrService).start();
    await vi.waitFor(() => { expect(store.selectedSite()).not.toBe(loaded); });
    await fixture.componentInstance.save();

    expect(updateSite).toHaveBeenCalledWith(site.id, expect.objectContaining({ name: 'Unsaved name' }));
  });
});
