import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgbActiveModal } from '@ng-bootstrap/ng-bootstrap';

/**
 * Asks for the admin token. Closes with the token, or is dismissed.
 */
@Component({
  selector: 'app-admin-token-prompt',
  imports: [FormsModule],
  templateUrl: './admin-token-prompt.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AdminTokenPrompt {
  protected readonly activeModal = inject(NgbActiveModal);
  protected readonly token = signal('');

  protected submit() {
    const token = this.token().trim();
    if (token) {
      this.activeModal.close(token);
    }
  }
}
