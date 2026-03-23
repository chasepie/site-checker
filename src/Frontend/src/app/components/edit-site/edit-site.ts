import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy, Component, effect,
  inject, linkedSignal, signal
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  disabled, form, FormField, max, min,
  pattern, required, SchemaPathTree, submit
} from '@angular/forms/signals';
import { NgbActiveModal } from '@ng-bootstrap/ng-bootstrap';
import { isDate } from 'lodash-es';
import { FormValid, setValidatedMetadata } from "../../directives/form-valid";
import { PushoverPriority, Site } from '../../generated/model';
import { SiteStore } from '../../services/site.store';
import { convertDateToTimeOnlyString } from '../../utilities/type-utils';

const TRUE = 'true' as const;
const FALSE = 'false' as const;

@Component({
  selector: 'app-edit-site',
  imports: [FormField, FormsModule, FormValid],
  templateUrl: './edit-site.html',
  styleUrl: './edit-site.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EditSite {
  protected readonly activeModal = inject(NgbActiveModal);
  private readonly _siteStore = inject(SiteStore);
  private readonly _linkedSite = linkedSignal(() => {
    const reqSite = this._siteStore.selectedSiteOrThrow();
    const schedule = reqSite.schedule;
    const psConfig = reqSite.pushoverConfig;
    const dcConfig = reqSite.discordConfig;

    return {
      ...reqSite,
      schedule: {
        ...schedule,
        interval: schedule.interval?.toString() ?? '',
      },
      pushoverConfig: {
        ...psConfig,
        successPriority: psConfig.successPriority ?? '' as const,
        failurePriority: psConfig.failurePriority ?? '' as const,
      },
      discordConfig: {
        ...dcConfig,
        channelId: dcConfig.channelId ?? '',
        successEnabled: dcConfig.successEnabled ? TRUE : FALSE,
        failureEnabled: dcConfig.failureEnabled ? TRUE : FALSE,
      },
    };
  });

  private readonly _originalSite = this._linkedSite();

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

  protected readonly siteForm = form(this._linkedSite, (path) => {
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

    this.configureScheduleForm(path);
    this.configureDiscordForm(path);

    setValidatedMetadata(path, this._linkedSite(), this._isValidated);
  });

  protected get pushoverForm() {
    return this.siteForm.pushoverConfig;
  }

  protected get discordForm() {
    return this.siteForm.discordConfig;
  }

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
  }

  private configureScheduleForm(path: SchemaPathTree<ReturnType<typeof this._linkedSite>>) {
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

  private configureDiscordForm(path: SchemaPathTree<ReturnType<typeof this._linkedSite>>) {
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

  public async save() {
    if (this.siteForm().invalid()) {
      this._isValidated.set(true);
      return;
    }

    await submit(this.siteForm, async (theForm) => {
      const formValue = theForm().value();
      const schedule = formValue.schedule;
      const poConfig = formValue.pushoverConfig;
      const dcConfig = formValue.discordConfig;

      const site: Site = {
        ...formValue,
        schedule: {
          ...schedule,
          interval: schedule.interval === ''
            ? null : Number(schedule.interval),
        },
        pushoverConfig: {
          ...poConfig,
          successPriority: poConfig.successPriority === ''
            ? null : poConfig.successPriority,
          failurePriority: poConfig.failurePriority === ''
            ? null : poConfig.failurePriority,
        },
        discordConfig: {
          ...dcConfig,
          channelId: dcConfig.channelId === ''
            ? null : dcConfig.channelId,
          successEnabled: dcConfig.successEnabled === TRUE,
          failureEnabled: dcConfig.failureEnabled === TRUE,
        },
      }

      try {
        await this._siteStore.updateSite(site);
      } catch (error) {
        console.error('Error updating site:', error);

        if (error instanceof HttpErrorResponse) {
          return {
            kind: 'http-error',
            message: error.message,
          }
        }

        if (error instanceof Error) {
          return {
            kind: 'save-error',
            message: error.message
          }
        }

        return {
          kind: 'save-error',
          message: 'An unknown error occurred while saving the site.'
        }
      }

      this.activeModal.close('Saved');
      return null;
    });
  }

  public resetForm() {
    this.siteForm().value.set(this._originalSite);
    this.siteForm().reset();
    this._isValidated.set(false);
  }
}
