import { computed, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  patchState, signalStore, withComputed, withHooks,
  withMethods, withProps, withState
} from '@ngrx/signals';
import { setAllEntities } from '@ngrx/signals/entities';
import { Site, SiteController, SiteUpdate } from '../generated/model';
import { withCrudEntities } from './base.store';
import { sitecheckSort, SiteCheckStore } from './site-check.store';

export const SiteStore = signalStore(
  { providedIn: 'root' },
  withCrudEntities<Site>('Site', Site),

  withState({
    _selectedSiteId: undefined as number | undefined,
    _totalCounts: {},
  }),

  withProps(() => ({
    _controller: inject(SiteController),
    _siteCheckStore: inject(SiteCheckStore)
  })),

  withComputed(store => {
    const selectedSite = computed(() => {
      const siteId = store._selectedSiteId();
      if (siteId === undefined) {
        return null;
      }
      return store.entityMap()[siteId];
    });

    const selectedSiteOrThrow = computed(() => {
      const site = selectedSite();
      if (!site) {
        throw new Error('No site selected');
      }
      return site;
    });

    const sitesWithLatestCheck = computed(() => {
      const sites = store.entities();

      return sites.map(site => ({
        ...site,
        latestCheck: store._siteCheckStore.entities()
          .filter(check => check.siteId === site.id)
          .sort(sitecheckSort)[0] ?? undefined,
      }));
    });

    return {
      selectedSite,
      selectedSiteOrThrow,
      sitesWithLatestCheck,
    };
  }),

  withMethods(store => ({
    selectSite: (site: Site) => {
      patchState(store, { _selectedSiteId: site.id });
    },

    updateSite: async (siteUpdate: SiteUpdate) => {
      const updated = await store._controller.updateSite(siteUpdate.id, siteUpdate);
      store._upsertInCache(updated);
    },
  })),

  withHooks(store => ({
    onInit: () => {
      store._signalrService.loadOnConnect(() => store._controller.getAllSites())
        .pipe(takeUntilDestroyed())
        .subscribe(sites => {
          // Replaced rather than merged, so a Site deleted while the hub was down goes too.
          patchState(store, setAllEntities(sites));
          for (const site of sites) {
            store._siteCheckStore.upsertFromSite(site);
          }
        });
    }
  })),
);
