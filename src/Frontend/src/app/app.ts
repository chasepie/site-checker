import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, PLATFORM_ID } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { Navbar } from './components/navbar/navbar';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, Navbar],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  private readonly _document = inject(DOCUMENT);
  private readonly _platformId = inject(PLATFORM_ID);

  constructor() {
    // Set up dark mode detection
    if (isPlatformBrowser(this._platformId)) {
      this.setupThemeMode();
    }
  }

  private setupThemeMode(): void {
    const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');

    // Set initial theme
    this.updateTheme(mediaQuery.matches);

    // Listen for changes
    mediaQuery.addEventListener('change', (e) => {
      this.updateTheme(e.matches);
    });
  }

  private updateTheme(isDark: boolean): void {
    const body = this._document.body;
    body.setAttribute('data-ag-theme-mode', isDark ? 'dark' : 'light');
  }
}
