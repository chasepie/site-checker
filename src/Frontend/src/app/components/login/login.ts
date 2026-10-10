import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, input, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
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
  private readonly _router = inject(Router);

  /** Where to go after logging in, from the query string. */
  public readonly returnUrl = input<string>();

  protected readonly password = signal('');
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);

  /** Logged in already, or login is off (Development): nothing to do here. */
  public ngOnInit() {
    if (this._auth.loggedIn()) {
      void this._router.navigateByUrl(this.safeReturnUrl());
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
      await this._router.navigateByUrl(this.safeReturnUrl());
    } catch (error) {
      this.error.set(describe(error));
      this.password.set('');
    } finally {
      this.busy.set(false);
    }
  }

  /** Only a path in this app, so a crafted link can't send you elsewhere after logging in. */
  private safeReturnUrl() {
    const url = this.returnUrl();
    return url?.startsWith('/') && !url.startsWith('//') && !url.startsWith('/login') ? url : '/';
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
