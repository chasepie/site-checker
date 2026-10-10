import { computed, inject } from '@angular/core';
import {
  patchState, signalStore, withComputed, withHooks,
  withMethods, withProps, withState
} from '@ngrx/signals';
import { Site, SiteController, SiteRequest } from '../generated/model';
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

    createSite: async (request: SiteRequest) => {
      const created = await store._controller.createSite(request);
      store._upsertInCache(created);
      return created;
    },

    /** Without a script in the request, the Site keeps its current one. */
    updateSite: async (id: number, request: SiteRequest) => {
      const updated = await store._controller.updateSite(id, request);
      store._upsertInCache(updated);
      return updated;
    },

    deleteSite: async (site: Site) => {
      await store._controller.deleteSite(site.id);
      // The delete broadcast skips this client, and never includes the Site's checks.
      store._removeFromCache(site.id);
      store._siteCheckStore.removeChecksForSite(site.id);
      if (store._selectedSiteId() === site.id) {
        patchState(store, { _selectedSiteId: undefined });
      }
    },
  })),

  withHooks(store => ({
    onInit: () => {
      void store._controller.getAllSites()
        .then(sites => {
          store._upsertInCache(sites);
          for (const site of sites) {
            store._siteCheckStore.upsertFromSite(site);
          }
        });
    }
  })),
);
