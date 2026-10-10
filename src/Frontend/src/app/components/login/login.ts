import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, input, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  templateUrl: './login.html',
  styleUrl: './login.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Login implements OnInit {
  private readonly _auth = inject(AuthService);

  /** Where to go after logging in, from the query string. */
  public readonly returnUrl = input<string>();

  protected readonly password = signal('');
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);

  /** Logged in already, or login is off (Development): nothing to do here. */
  public ngOnInit() {
    if (this._auth.loggedIn()) {
      window.location.assign(this.safeReturnUrl());
    }
  }

  protected async submit() {
    const password = this.password().trim();
    if (!password || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      await this._auth.login(password);
    } catch (error) {
      this.error.set(describe(error));
      this.password.set('');
      this.busy.set(false);
      return;
    }

    // A full page load rather than the router: the return URL may be a server page (Scalar), and
    // the app starts fresh under the new session.
    window.location.assign(this.safeReturnUrl());
  }

  /**
   * The return URL if it's on this site, so a crafted link can't send you elsewhere after logging
   * in. Resolving it against this origin catches what prefix checks miss, such as `/\evil.com`,
   * which browsers treat as `//evil.com`.
   */
  private safeReturnUrl() {
    const url = this.returnUrl();
    if (!url) {
      return '/';
    }
    try {
      const resolved = new URL(url, window.location.origin);
      const path = resolved.pathname + resolved.search + resolved.hash;
      return resolved.origin === window.location.origin && !resolved.pathname.startsWith('/login') ? path : '/';
    } catch {
      return '/';
    }
  }
}

function describe(error: unknown) {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 401) {
      return 'That password is wrong.';
    }
    if (error.status === 429) {
      return 'Too many attempts. Wait a minute, then try again.';
    }
  }
  return 'Couldn\'t log in. Check that the server is running, then try again.';
}
