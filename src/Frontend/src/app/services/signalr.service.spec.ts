import { ErrorHandler } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { SignalrService } from './signalr.service';

describe('SignalrService.loadOnConnect', () => {
  let errorHandler: { handleError: ReturnType<typeof vi.fn> };
  let signalr: SignalrService;
  let reconnect: () => void;
  let values: string[];

  beforeEach(() => {
    // A connection that connects at once, and reconnects when the test says.
    const connection = {
      state: HubConnectionState.Disconnected,
      start: vi.fn().mockResolvedValue(undefined),
      onreconnected: (callback: () => void) => { reconnect = callback; },
    };
    vi.spyOn(HubConnectionBuilder.prototype, 'build').mockReturnValue(connection as unknown as HubConnection);
    errorHandler = { handleError: vi.fn() };
    TestBed.configureTestingModule({
      providers: [{ provide: ErrorHandler, useValue: errorHandler }],
    });
    signalr = TestBed.inject(SignalrService);
    values = [];
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('loads now, and again each time the hub connects or reconnects', async () => {
    const load = vi.fn<() => Promise<string>>()
      .mockResolvedValueOnce('at startup')
      .mockResolvedValueOnce('after connecting')
      .mockResolvedValueOnce('after reconnecting');
    signalr.loadOnConnect(load).subscribe(value => values.push(value));
    await vi.waitFor(() => { expect(values).toEqual(['at startup']); });

    await signalr.start();
    await vi.waitFor(() => { expect(values).toEqual(['at startup', 'after connecting']); });
    reconnect();

    await vi.waitFor(() => { expect(values).toEqual(['at startup', 'after connecting', 'after reconnecting']); });
  });

  it('reports a failed load, and loads again on the next connection', async () => {
    const error = new Error('The server is unreachable.');
    const load = vi.fn<() => Promise<string>>()
      .mockRejectedValueOnce(error)
      .mockResolvedValueOnce('after connecting');
    signalr.loadOnConnect(load).subscribe(value => values.push(value));
    await vi.waitFor(() => { expect(errorHandler.handleError).toHaveBeenCalledWith(error); });

    await signalr.start();

    await vi.waitFor(() => { expect(values).toEqual(['after connecting']); });
  });

  it('drops a load that the next one overtakes', async () => {
    let resolveStale = (_value: string) => { /* set below */ };
    const load = vi.fn<() => Promise<string>>()
      .mockReturnValueOnce(new Promise(resolve => { resolveStale = resolve; }))
      .mockResolvedValueOnce('fresh');
    signalr.loadOnConnect(load).subscribe(value => values.push(value));

    await signalr.start();
    await vi.waitFor(() => { expect(values).toEqual(['fresh']); });
    resolveStale('stale');
    await new Promise(resolve => setTimeout(resolve));

    expect(values).toEqual(['fresh']);
  });
});
