import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { Site, SiteCheckController, SiteController, SiteRequest } from '../../generated/model';
import { SignalrService } from '../../services/signalr.service';
import { SiteStore } from '../../services/site.store';
import { SiteEditor } from './site-editor';

const site: Site = {
  id: 1,
  name: 'Saved name',
  url: 'https://example.com',
  useVpn: false,
  alwaysTakeScreenshot: false,
  knownFailuresThreshold: 3,
  timeoutSeconds: null,
  schedule: { enabled: false, start: null, end: null, interval: null },
  pushoverConfig: { successPriority: null, failurePriority: null },
  discordConfig: { successEnabled: false, failureEnabled: false, channelId: null },
  scraper: {
    kind: 'Script',
    script: { fileName: 'Example.cs', sourceHash: 'hash', uploadedAt: '2026-01-01T00:00:00Z' },
  },
  siteChecks: [],
};

describe('SiteEditor', () => {
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
    const getSiteScript = vi.fn(() => Promise.resolve({ siteId: site.id, source: '// The script' }));
    const updateSite = vi.fn((_id: number, request: SiteRequest) => Promise.resolve({ ...site, ...request, scraper: site.scraper }));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: SiteController, useValue: { getAllSites, getSiteScript, updateSite } },
        { provide: SiteCheckController, useValue: {} },
      ],
    });
    const store = TestBed.inject(SiteStore);
    await vi.waitFor(() => { expect(store.entities()).toHaveLength(1); });
    const loaded = store.entityMap()[site.id];
    const fixture = TestBed.createComponent(SiteEditor);
    fixture.componentRef.setInput('id', String(site.id));
    await fixture.whenStable();
    const name = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('#name');
    if (!name) {
      throw new Error('The name field is missing.');
    }
    name.value = 'Unsaved name';
    name.dispatchEvent(new Event('input'));

    await TestBed.inject(SignalrService).start();
    await vi.waitFor(() => { expect(store.entityMap()[site.id]).not.toBe(loaded); });
    await fixture.componentInstance.save();

    expect(updateSite).toHaveBeenCalledWith(site.id, expect.objectContaining({ name: 'Unsaved name' }));
  });
});
