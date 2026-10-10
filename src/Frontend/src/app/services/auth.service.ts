import { inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthController } from '../generated/model';
import { SignalrService } from './signalr.service';

/**
 * Delays before each attempt to reconnect the hub after it closes for good. The last one repeats
 * until it connects, so live updates resume after an outage of any length.
 */
const RECONNECT_DELAYS_MS = [0, 2_000, 10_000, 30_000];

/**
 * The login session. The server keeps it in an HttpOnly cookie; this tracks whether there is one,
 * and starts and stops the SignalR connection with it, since the hub requires a login.
 *
 * Every session boundary (login, logout, the session ending) reloads the page, so no data loaded
 * under one session lingers in the stores under the next.
 */
@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly _controller = inject(AuthController);
  private readonly _signalr = inject(SignalrService);
  private readonly _router = inject(Router);
  private _loggingOut = false;

  /** `false` only in Development without `ADMIN_PASSWORD`, where login is off. */
  public readonly loginRequired = signal(true);
  public readonly loggedIn = signal(false);

  constructor() {
    // Any logout closes every connection on the server. A browser still logged in reconnects;
    // one that isn't goes to the login page.
    this._signalr.onReconnecting(() => void this.checkSessionStillOpen());
    this._signalr.onClose(() => void this.reconnectIfStillLoggedIn());
  }

  /** Reads the session from the server, which also issues the antiforgery token for writes. */
  public async refresh() {
    const session = await this._controller.getSession();
    this.loginRequired.set(session.loginRequired);
    this.loggedIn.set(session.loggedIn);
    return session.loggedIn;
  }

  /** At startup: connects to the hub if logged in. */
  public async initialize() {
    if (await this.refresh()) {
      await this._signalr.start();
    }
  }

  /** Logs in. The caller then loads the page it's going to, which starts the hub. */
  public async login(password: string) {
    await this._controller.login({ password });
  }

  /** Ends the session and reloads at the login page. */
  public async logout() {
    this._loggingOut = true;
    try {
      await this._signalr.stop();
      await this._controller.logout();
    } finally {
      window.location.assign('/login');
    }
  }

  /** After the server answers 401: the session ended, so reload at the login page. */
  public sessionEnded() {
    this.loggedIn.set(false);
    const current = this._router.url;
    if (!current.startsWith('/login')) {
      window.location.assign(`/login?returnUrl=${encodeURIComponent(current)}`);
    }
  }

  private async checkSessionStillOpen() {
    try {
      if (!await this.refresh()) {
        this.sessionEnded();
      }
    } catch {
      // The server is unreachable; reconnecting carries on, and the next request will tell.
    }
  }

  /**
   * The hub closed for good: the server closes connections on any logout, which also stops
   * automatic reconnecting. Starts it again while the session is still valid.
   */
  private async reconnectIfStillLoggedIn() {
    for (let attempt = 0; ; attempt++) {
      const delay = RECONNECT_DELAYS_MS[Math.min(attempt, RECONNECT_DELAYS_MS.length - 1)];
      await new Promise(resolve => setTimeout(resolve, delay));
      if (this._loggingOut) {
        return;
      }
      try {
        if (!await this.refresh()) {
          this.sessionEnded();
          return;
        }
        await this._signalr.start();
        return;
      } catch {
        // The server is unreachable or restarting; try again after the next delay.
      }
    }
  }
}
