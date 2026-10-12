import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { form, FormField, FormRoot, required } from '@angular/forms/signals';
import { AuthService } from '../../services/auth.service';

/** The kind of error the form shows when the server turns the login down. */
const LOGIN_FAILED = 'loginFailed';

@Component({
  selector: 'app-login',
  imports: [FormField, FormRoot],
  templateUrl: './login.html',
  styleUrl: './login.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Login {
  private readonly _auth = inject(AuthService);

  /** Where to go after logging in, from the query string. */
  public readonly returnUrl = input<string>();

  protected readonly loginForm = form(
    signal({ password: '' }),
    path => {
      required(path.password, { message: 'Enter the password.' });
    },
    {
      submission: {
        action: async field => {
          try {
            await this._auth.login(field.password().value());
          } catch (error) {
            // Clears the password and puts the cursor back in it for the next attempt.
            field.password().value.set('');
            field.password().focusBoundControl();
            return { kind: LOGIN_FAILED, message: describe(error), fieldTree: field.password };
          }
          this._auth.goToReturnUrl(this.returnUrl());
          return undefined;
        },
      },
    },
  );

  /**
   * The password's error once it's been touched. The server's answer comes first: the password is
   * cleared after a failed login, which also makes it "required".
   */
  protected readonly passwordError = computed(() => {
    const password = this.loginForm.password();
    if (!password.touched()) {
      return null;
    }
    const errors = password.errors();
    return (errors.find(error => error.kind === LOGIN_FAILED) ?? errors.at(0))?.message ?? null;
  });
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
