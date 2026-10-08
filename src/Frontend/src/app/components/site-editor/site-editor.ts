import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy, Component, computed, DestroyRef, effect,
  inject, input, linkedSignal, resource, signal, untracked
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  disabled, form, FormField, max, min,
  pattern, required, SchemaPathTree, submit
} from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { isDate } from 'lodash-es';
import { filter, firstValueFrom, Subject, takeUntil } from 'rxjs';
import { FormValid, setValidatedMetadata } from '../../directives/form-valid';
import {
  PushoverPriority, ScraperKind, ScriptDiagnostic, ScriptUpload, Site,
  SiteController, SiteRequest, SiteValidationResult, TestRunResult
} from '../../generated/model';
import { SignalrService } from '../../services/signalr.service';
import { SiteStore } from '../../services/site.store';
import { requestedActionLabel, scrapeOutcomeLabel } from '../../utilities/labels';
import { convertDateToTimeOnlyString } from '../../utilities/type-utils';

const TRUE = 'true' as const;
const FALSE = 'false' as const;

/**
 * The form's model: a Site's settings, with numbers and optional values as the strings the
 * inputs edit. A missing Site gives the Create Site page's defaults.
 */
function toFormModel(site: Site | undefined) {
  const schedule = site?.schedule;
  const psConfig = site?.pushoverConfig;
  const dcConfig = site?.discordConfig;

  return {
    name: site?.name ?? '',
    url: site?.url ?? '',
    useVpn: site?.useVpn ?? false,
    alwaysTakeScreenshot: site?.alwaysTakeScreenshot ?? false,
    knownFailuresThreshold: site?.knownFailuresThreshold ?? 5,
    timeoutSeconds: site?.timeoutSeconds?.toString() ?? '',
    schedule: {
      enabled: schedule?.enabled ?? false,
      start: schedule?.start ?? null,
      end: schedule?.end ?? null,
      interval: schedule?.interval?.toString() ?? '',
    },
    pushoverConfig: {
      successPriority: psConfig?.successPriority ?? '' as const,
      failurePriority: psConfig?.failurePriority ?? '' as const,
    },
    discordConfig: {
      channelId: dcConfig?.channelId ?? '',
      successEnabled: dcConfig?.successEnabled ? TRUE : FALSE,
      failureEnabled: dcConfig?.failureEnabled ? TRUE : FALSE,
    },
  };
}

type SiteFormModel = ReturnType<typeof toFormModel>;

function toSiteRequest(value: SiteFormModel, id: number, script: ScriptUpload | null): SiteRequest {
  const schedule = value.schedule;
  const poConfig = value.pushoverConfig;
  const dcConfig = value.discordConfig;

  return {
    id,
    name: value.name,
    url: value.url,
    useVpn: value.useVpn,
    alwaysTakeScreenshot: value.alwaysTakeScreenshot,
    knownFailuresThreshold: value.knownFailuresThreshold,
    timeoutSeconds: toNumberOrNull(value.timeoutSeconds),
    schedule: {
      ...schedule,
      interval: toNumberOrNull(schedule.interval),
    },
    pushoverConfig: {
      successPriority: poConfig.successPriority === '' ? null : poConfig.successPriority,
      failurePriority: poConfig.failurePriority === '' ? null : poConfig.failurePriority,
    },
    discordConfig: {
      channelId: dcConfig.channelId === '' ? null : dcConfig.channelId,
      successEnabled: dcConfig.successEnabled === TRUE,
      failureEnabled: dcConfig.failureEnabled === TRUE,
    },
    // Without a new file, the Site keeps its current script.
    scraper: { kind: ScraperKind.enum.Script, script },
  };
}

function toNumberOrNull(value: string): number | null {
  return value === '' ? null : Number(value);
}

type TestRunState =
  | { status: 'idle' }
  | { status: 'running'; testRunId: string }
  | { status: 'done'; result: TestRunResult }
  | { status: 'error'; message: string };

/**
 * Creates or edits a Site: its settings, its script, and a Test Run of exactly what's on the page.
 */
@Component({
  selector: 'app-site-editor',
  imports: [FormField, FormsModule, FormValid, RouterLink, DatePipe, DecimalPipe],
  templateUrl: './site-editor.html',
  styleUrl: './site-editor.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SiteEditor {
  private readonly _siteStore = inject(SiteStore);
  private readonly _controller = inject(SiteController);
  private readonly _signalr = inject(SignalrService);
  private readonly _router = inject(Router);

  /** Stops waiting for a Test Run result that's no longer wanted. */
  private readonly _stopWaiting$ = new Subject<void>();

  /** The Site's ID, from the route. Absent on the Create Site page. */
  public readonly id = input<string>();

  protected readonly siteId = computed(() => {
    const id = this.id();
    return id == null ? null : Number(id);
  });

  protected readonly isNew = computed(() => this.siteId() == null);

  protected readonly site = computed(() => {
    const id = this.siteId();
    return id == null ? undefined : this._siteStore.entityMap()[id];
  });

  // Reset when a different Site loads, but not on every broadcast update of the same one.
  private readonly _model = linkedSignal({
    source: () => this.site()?.id,
    computation: () => toFormModel(untracked(this.site)),
  });

  protected readonly _isValidated = signal(false);

  protected readonly priorities = [
    { value: '', label: 'Off' },
    { value: PushoverPriority.enum.Lowest, label: 'Lowest' },
    { value: PushoverPriority.enum.Low, label: 'Low' },
    { value: PushoverPriority.enum.Normal, label: 'Normal' },
    { value: PushoverPriority.enum.High, label: 'High' },
    { value: PushoverPriority.enum.Emergency, label: 'Emergency' },
  ];

  protected readonly onOffOptions = [
    { value: TRUE, label: 'On' },
    { value: FALSE, label: 'Off' },
  ];

  protected readonly siteForm = form(this._model, (path) => {
    pattern(path.url, /^(https?:\/\/)/, {
      message: 'URL must start with http:// or https://'
    });

    required(path.url, {
      message: 'URL is required.'
    });
    required(path.name, {
      message: 'Name is required.'
    });

    min(path.knownFailuresThreshold, 1, {
      message: 'Known failures threshold must be at least 1.'
    });
    max(path.knownFailuresThreshold, 999, {
      message: 'Known failures threshold must be at most 999.'
    });

    pattern(path.timeoutSeconds, /^([1-9]\d*)?$/, {
      message: 'Timeout must be a whole number of seconds, or blank for the default.'
    });

    this.configureScheduleForm(path);
    this.configureDiscordForm(path);

    setValidatedMetadata(path, this._model(), this._isValidated);
  });

  protected get pushoverForm() {
    return this.siteForm.pushoverConfig;
  }

  protected get discordForm() {
    return this.siteForm.discordConfig;
  }

  /** A `.cs` file picked on this page and not saved yet. */
  protected readonly upload = signal<ScriptUpload | null>(null);

  private readonly _savedScript = resource({
    params: () => this.siteId() ?? undefined,
    loader: ({ params: id }) => this._controller.getSiteScript(id),
  });

  protected readonly scriptFileName = computed(() =>
    this.upload()?.fileName ?? this.site()?.scraper.script?.fileName ?? null);

  protected readonly scriptSource = computed(() => {
    const upload = this.upload();
    if (upload) {
      return upload.source;
    }
    return this._savedScript.hasValue() ? this._savedScript.value().source : null;
  });

  protected readonly uploadedAt = computed(() =>
    this.upload() ? null : this.site()?.scraper.script?.uploadedAt ?? null);

  /** Why the last save or Test Run was rejected. */
  protected readonly errors = signal<string[]>([]);
  protected readonly diagnostics = signal<ScriptDiagnostic[]>([]);

  protected readonly testRun = signal<TestRunState>({ status: 'idle' });

  protected readonly testRunScreenshot = computed(() => {
    const run = this.testRun();
    return run.status === 'done' && run.result.screenshot
      ? `data:image/png;base64,${run.result.screenshot}`
      : null;
  });

  protected readonly requestedActionLabel = requestedActionLabel;
  protected readonly scrapeOutcomeLabel = scrapeOutcomeLabel;

  public constructor() {
    effect(() => {
      const startDate = this.siteForm.schedule.start().value();
      if (isDate(startDate)) {
        const dateStr = convertDateToTimeOnlyString(startDate);
        this.siteForm.schedule.start().value.set(dateStr);
      }
    });

    effect(() => {
      const endDate = this.siteForm.schedule.end().value();
      if (isDate(endDate)) {
        const dateStr = convertDateToTimeOnlyString(endDate);
        this.siteForm.schedule.end().value.set(dateStr);
      }
    });

    inject(DestroyRef).onDestroy(() => {
      this._stopWaiting$.next();
      this._stopWaiting$.complete();
    });
  }

  private configureScheduleForm(path: SchemaPathTree<SiteFormModel>) {
    const schedule = path.schedule;

    min(schedule.interval, 1, {
      message: 'Schedule interval must be at least 1 minute.'
    });

    disabled(schedule.start, (ctx) => !ctx.valueOf(schedule.enabled));
    disabled(schedule.end, (ctx) => !ctx.valueOf(schedule.enabled));
    disabled(schedule.interval, (ctx) => !ctx.valueOf(schedule.enabled));

    required(schedule.start, {
      when: (ctx) => ctx.valueOf(schedule.enabled),
      message: 'Schedule start is required when scheduling is enabled.',
    });

    required(schedule.end, {
      when: (ctx) => ctx.valueOf(schedule.enabled),
      message: 'Schedule end is required when scheduling is enabled.',
    });

    required(schedule.interval, {
      when: (ctx) => ctx.valueOf(schedule.enabled),
      message: 'Schedule interval is required when scheduling is enabled.',
    });
  }

  private configureDiscordForm(path: SchemaPathTree<SiteFormModel>) {
    const discord = path.discordConfig;

    required(discord.channelId, {
      when: (ctx) => ctx.valueOf(discord.successEnabled) === TRUE
        || ctx.valueOf(discord.failureEnabled) === TRUE,
      message: 'Discord Channel ID is required when Discord notifications are enabled.',
    });

    pattern(discord.channelId, /^\d+$/, {
      message: 'Discord Channel ID must be a numeric string.'
    });
  }

  protected async onScriptSelected(event: Event) {
    const fileInput = event.target as HTMLInputElement;
    const file = fileInput.files?.[0];
    if (!file) {
      return;
    }

    this.upload.set({ fileName: file.name, source: await file.text() });
    this.diagnostics.set([]);
    // So choosing the same file again, after editing it, still fires a change.
    fileInput.value = '';
  }

  protected downloadScript() {
    const source = this.scriptSource();
    const fileName = this.scriptFileName();
    if (source == null || fileName == null) {
      return;
    }

    const url = URL.createObjectURL(new Blob([source], { type: 'text/plain' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.click();
    URL.revokeObjectURL(url);
  }

  public async save() {
    if (this.siteForm().invalid()) {
      this._isValidated.set(true);
      return;
    }

    await submit(this.siteForm, async (theForm) => {
      const id = this.siteId();
      const request = toSiteRequest(theForm().value(), id ?? 0, this.upload());
      this.errors.set([]);
      this.diagnostics.set([]);

      try {
        const saved = id == null
          ? await this._siteStore.createSite(request)
          : await this._siteStore.updateSite(id, request);
        this.upload.set(null);
        this._siteStore.selectSite(saved);
        await this._router.navigate(['/']);
      } catch (error) {
        this.showRejection(error);
      }
      return null;
    });
  }

  /**
   * Runs exactly what's on the page: the picked or saved script with this form's URL, VPN,
   * timeout and screenshot settings. The result arrives over SignalR, to this connection only.
   */
  protected async runTest() {
    const source = this.scriptSource();
    const fileName = this.scriptFileName();
    if (source == null || fileName == null) {
      this.testRun.set({ status: 'error', message: 'Choose a script file first.' });
      return;
    }

    const connectionId = this._signalr.connectionId;
    if (!connectionId) {
      this.testRun.set({ status: 'error', message: 'Not connected to the server. Reload the page and try again.' });
      return;
    }

    this._stopWaiting$.next();
    const value = this.siteForm().value();
    const testRunId = crypto.randomUUID();

    // Listen before sending, so a fast result can't arrive before it's expected.
    const result = firstValueFrom(
      this._signalr.testRunCompleted$.pipe(
        filter(r => r.testRunId === testRunId),
        takeUntil(this._stopWaiting$),
      ),
      { defaultValue: null },
    );

    this.testRun.set({ status: 'running', testRunId });
    this.errors.set([]);
    this.diagnostics.set([]);
    try {
      await this._controller.startTestRun({
        testRunId,
        connectionId,
        name: value.name === '' ? null : value.name,
        url: value.url,
        useVpn: value.useVpn,
        alwaysTakeScreenshot: value.alwaysTakeScreenshot,
        timeoutSeconds: toNumberOrNull(value.timeoutSeconds),
        scraper: { kind: ScraperKind.enum.Script, script: { fileName, source } },
      });
    } catch (error) {
      this._stopWaiting$.next();
      this.testRun.set({ status: 'idle' });
      this.showRejection(error);
      return;
    }

    const completed = await result;
    const current = this.testRun();
    if (completed && current.status === 'running' && current.testRunId === testRunId) {
      this.testRun.set({ status: 'done', result: completed });
    }
  }

  protected stopWaitingForTestRun() {
    this._stopWaiting$.next();
    this.testRun.set({ status: 'idle' });
  }

  private showRejection(error: unknown) {
    if (error instanceof HttpErrorResponse && error.status === 400) {
      const rejection = SiteValidationResult.safeParse(error.error);
      if (rejection.success) {
        this.errors.set(rejection.data.errors);
        this.diagnostics.set(rejection.data.diagnostics);
        return;
      }
    }

    console.error('The server rejected the request:', error);
    const message = error instanceof HttpErrorResponse || error instanceof Error
      ? error.message
      : 'Something went wrong.';
    this.errors.set([message]);
    this.diagnostics.set([]);
  }

  public resetForm() {
    this.siteForm().value.set(toFormModel(this.site()));
    this.siteForm().reset();
    this.upload.set(null);
    this.errors.set([]);
    this.diagnostics.set([]);
    this._isValidated.set(false);
  }
}
