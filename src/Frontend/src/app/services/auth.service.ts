import { DOCUMENT } from '@angular/common';
import { computed, inject, Injectable, signal } from '@angular/core';
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
  private readonly _location = inject(DOCUMENT).location;
  private readonly _loginRequired = signal(true);
  private readonly _loggedIn = signal(false);
  private _loggingOut = false;

  /** `false` only in Development without `ADMIN_PASSWORD`, where login is off. */
  public readonly loginRequired = this._loginRequired.asReadonly();
  public readonly loggedIn = this._loggedIn.asReadonly();
  /** Whether there's a session to log out of. */
  public readonly canLogOut = computed(() => this._loginRequired() && this._loggedIn());

  constructor() {
    // Any logout closes every connection on the server. A browser still logged in reconnects;
    // one that isn't goes to the login page.
    this._signalr.onReconnecting(() => void this.checkSessionStillOpen());
    this._signalr.onClose(() => void this.reconnectIfStillLoggedIn());
  }

  /** Reads the session from the server, which also issues the antiforgery token for writes. */
  public async refresh() {
    const session = await this._controller.getSession();
    this._loginRequired.set(session.loginRequired);
    this._loggedIn.set(session.loggedIn);
    return session.loggedIn;
  }

  /**
   * At startup: connects to the hub if logged in. If it can't, the app loads anyway and keeps
   * trying, rather than failing startup and leaving a blank page.
   */
  public async initialize() {
    if (await this.refresh()) {
      try {
        await this._signalr.start();
      } catch {
        void this.reconnectIfStillLoggedIn();
      }
    }
  }

  /** Logs in. The caller then loads the page it's going to, which starts the hub. */
  public async login(password: string) {
    await this._controller.login({ password });
  }

  /**
   * Loads the page a login was for, or the dashboard if that isn't on this site. A full page load
   * rather than the router: the return URL may be a server page (Scalar), and the app starts fresh
   * under the new session.
   */
  public goToReturnUrl(returnUrl: string | null | undefined) {
    this._location.assign(this.safeReturnUrl(returnUrl));
  }

  /** Ends the session and reloads at the login page. */
  public async logout() {
    this._loggingOut = true;
    try {
      await this._signalr.stop();
      await this._controller.logout();
    } finally {
      this._location.assign('/login');
    }
  }

  /** After the server answers 401: the session ended, so reload at the login page. */
  public sessionEnded() {
    this._loggedIn.set(false);
    const current = this._router.url;
    if (!current.startsWith('/login')) {
      this._location.assign(`/login?returnUrl=${encodeURIComponent(current)}`);
    }
  }

  /**
   * The return URL if it's on this site, so a crafted link can't send you elsewhere after logging
   * in. Resolving it against this origin catches what prefix checks miss, such as `/\evil.com`,
   * which browsers treat as `//evil.com`. It returns the resolved absolute URL, never just its path:
   * `/.//evil.com` resolves on this origin to the path `//evil.com`, which leaves the site if it's
   * loaded on its own.
   */
  private safeReturnUrl(returnUrl: string | null | undefined) {
    if (!returnUrl) {
      return '/';
    }
    try {
      const resolved = new URL(returnUrl, this._location.origin);
      return resolved.origin === this._location.origin && !resolved.pathname.startsWith('/login') ? resolved.href : '/';
    } catch {
      return '/';
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
   * The hub closed for good (the server closes connections on any logout, which also stops
   * automatic reconnecting), or didn't start. Starts it again while the session is still valid.
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
