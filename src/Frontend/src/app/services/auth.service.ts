import { inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthController } from '../generated/model';
import { SignalrService } from './signalr.service';

/**
 * The login session. The server keeps it in an HttpOnly cookie; this tracks whether there is one,
 * and starts and stops the SignalR connection with it, since the hub requires a login.
 */
@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly _controller = inject(AuthController);
  private readonly _signalr = inject(SignalrService);
  private readonly _router = inject(Router);

  /** `false` only in Development without `ADMIN_PASSWORD`, where login is off. */
  public readonly loginRequired = signal(true);
  public readonly loggedIn = signal(false);

  constructor() {
    // The server drops every connection on any logout. Check at once rather than wait out the
    // reconnect delays: a browser still logged in carries on reconnecting; one that isn't, stops.
    this._signalr.onReconnecting(() => void this.checkSessionStillOpen());
    this._signalr.onClose(() => void this.checkSessionStillOpen());
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

  /** Logs in, refreshes the session (and its antiforgery token) and connects to the hub. */
  public async login(password: string) {
    await this._controller.login({ password });
    await this.refresh();
    await this._signalr.start();
  }

  /**
   * Ends the session and reloads at the login page, so nothing loaded while logged in stays in
   * memory.
   */
  public async logout() {
    try {
      await this._signalr.stop();
      await this._controller.logout();
    } finally {
      window.location.assign('/login');
    }
  }

  /** After the server answers 401: the session ended, so go log in again. */
  public async sessionEnded() {
    this.loggedIn.set(false);
    await this._signalr.stop();
    if (!this._router.url.startsWith('/login')) {
      await this._router.navigate(['/login'], { queryParams: { returnUrl: this._router.url } });
    }
  }

  private async checkSessionStillOpen() {
    try {
      if (!await this.refresh()) {
        await this.sessionEnded();
      }
    } catch {
      // The server is unreachable; the next request will tell.
    }
  }
}
