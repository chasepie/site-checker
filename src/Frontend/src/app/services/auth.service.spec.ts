import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { DefaultUrlSerializer, Router } from '@angular/router';
import { AuthController } from '../generated/model';
import { AuthService } from './auth.service';
import { SignalrService } from './signalr.service';

describe('AuthService', () => {
  let controller: { getSession: ReturnType<typeof vi.fn>; logout: ReturnType<typeof vi.fn> };
  let signalr: { start: ReturnType<typeof vi.fn>; stop: ReturnType<typeof vi.fn> };
  let location: { assign: ReturnType<typeof vi.fn>; origin: string };
  let router: { url: string; parseUrl: (url: string) => ReturnType<DefaultUrlSerializer['parse']> };
  let auth: AuthService;

  beforeEach(() => {
    controller = {
      getSession: vi.fn().mockResolvedValue({ loginRequired: true, loggedIn: true }),
      logout: vi.fn(),
    };
    signalr = {
      start: vi.fn().mockResolvedValue(undefined),
      stop: vi.fn().mockResolvedValue(undefined),
    };
    location = { assign: vi.fn(), origin: 'https://sitechecker.lan' };
    router = { url: '/', parseUrl: url => new DefaultUrlSerializer().parse(url) };
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthController, useValue: controller },
        { provide: SignalrService, useValue: { ...signalr, onClose: vi.fn(), onReconnecting: vi.fn() } },
        { provide: Router, useValue: router },
        { provide: DOCUMENT, useValue: { location } },
      ],
    });
    auth = TestBed.inject(AuthService);
  });

  it('reloads at the login page once the server ends the session', async () => {
    controller.logout.mockResolvedValue(undefined);

    await auth.logout();

    expect(signalr.stop).toHaveBeenCalled();
    expect(location.assign).toHaveBeenCalledWith('/login');
  });

  it('reloads at the login page when the session had already ended', async () => {
    controller.logout.mockRejectedValue(new HttpErrorResponse({ status: 401 }));

    await auth.logout();

    expect(location.assign).toHaveBeenCalledWith('/login');
  });

  it('stays, throws and reconnects the hub when the server refuses the logout', async () => {
    const error = new HttpErrorResponse({ status: 400 });
    controller.logout.mockRejectedValue(error);

    await expect(auth.logout()).rejects.toBe(error);
    await vi.waitFor(() => { expect(signalr.start).toHaveBeenCalled(); });

    expect(location.assign).not.toHaveBeenCalled();
    expect(controller.getSession).toHaveBeenCalled();
  });

  it('can be retried after a refused logout', async () => {
    controller.logout.mockRejectedValueOnce(new HttpErrorResponse({ status: 500 })).mockResolvedValueOnce(undefined);

    await expect(auth.logout()).rejects.toBeInstanceOf(HttpErrorResponse);
    await auth.logout();

    expect(controller.logout).toHaveBeenCalledTimes(2);
    expect(location.assign).toHaveBeenCalledWith('/login');
  });

  it('loads when the server is unreachable at startup, then goes on once the session is valid', async () => {
    controller.getSession.mockRejectedValueOnce(new HttpErrorResponse({ status: 0 }));
    // Where the guard sends a browser whose session is unknown.
    router.url = '/login?returnUrl=%2Fhistory';

    await auth.initialize();
    await vi.waitFor(() => { expect(location.assign).toHaveBeenCalledWith('https://sitechecker.lan/history'); });

    expect(signalr.start).not.toHaveBeenCalled();
  });
});
