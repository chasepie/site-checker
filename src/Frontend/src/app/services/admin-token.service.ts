import { inject, Injectable } from '@angular/core';
import { NgbModal } from '@ng-bootstrap/ng-bootstrap';
import { AdminTokenPrompt } from '../components/admin-token-prompt/admin-token-prompt';

const STORAGE_KEY = 'siteChecker.adminToken';

/**
 * The admin token the server requires before saving a Site or starting a Test Run. It's kept in
 * this browser's storage, and asked for when the server rejects a request without it.
 */
@Injectable({
  providedIn: 'root'
})
export class AdminTokenService {
  private readonly _modalService = inject(NgbModal);
  private _token: string | null = readStoredToken();
  private _prompt: Promise<string | null> | null = null;

  public get token(): string | null {
    return this._token;
  }

  public clear() {
    this._token = null;
    try {
      localStorage.removeItem(STORAGE_KEY);
    } catch {
      // Storage is unavailable, so there's nothing stored.
    }
  }

  /**
   * Asks for the token and stores it. Requests rejected at the same time share one prompt.
   * @returns The token, or `null` when the prompt was dismissed.
   */
  public prompt(): Promise<string | null> {
    this._prompt ??= this.openPrompt().finally(() => {
      this._prompt = null;
    });
    return this._prompt;
  }

  private async openPrompt(): Promise<string | null> {
    let token: string;
    try {
      token = await (this._modalService.open(AdminTokenPrompt).result as Promise<string>);
    } catch {
      return null; // Dismissed
    }

    this._token = token;
    try {
      localStorage.setItem(STORAGE_KEY, token);
    } catch {
      // Kept for this page only.
    }
    return token;
  }
}

function readStoredToken(): string | null {
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}
