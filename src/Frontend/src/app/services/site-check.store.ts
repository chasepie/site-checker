import { computed, effect, inject, resource } from '@angular/core';
import { patchState, signalStore, withComputed, withHooks, withMethods, withProps, withState } from '@ngrx/signals';
import { Site, SiteCheck, SiteCheckController } from '../generated/model';
import { withCrudEntities } from './base.store';

interface SiteCheckFilter {
  siteId?: number;
  pageNumber: number;
  pageSize: number;
}

export const sitecheckSort = (a: SiteCheck, b: SiteCheck) => {
  return b.id - a.id;
}

export const SiteCheckStore = signalStore(
  { providedIn: 'root' },
  withCrudEntities<SiteCheck>('SiteCheck', SiteCheck),

  withState({
    _getAllFilter: {
      pageNumber: 0,
      pageSize: 10,
      siteId: undefined,
    } satisfies SiteCheckFilter as SiteCheckFilter,
    _totalItemCounts: {} as Record<Site['id'], number>,
  }),

  withProps(store => {
    const _controller = inject(SiteCheckController);

    const _resource = resource({
      params: () => ({ filter: store._getAllFilter() }),
      loader: ({ params }) => {
        if (params.filter.siteId == null) {
          return Promise.resolve(null);
        }

        return _controller.getAllSiteChecks(
          params.filter.siteId,
          params.filter.pageNumber,
          params.filter.pageSize
        );
      }
    })

    return {
      _controller,
      _resource,
    };
  }),

  withComputed(store => {
    const totalItemCount = computed(() => {
      const siteId = store._getAllFilter().siteId;
      if (siteId == null) {
        return -1;
      }

      return store._totalItemCounts()[siteId] ?? -1;
    });

    const filteredEntities = computed(() => {
      const filter = store._getAllFilter();
      return store.entities()
        .filter(c => c.siteId === filter.siteId)
        .sort(sitecheckSort)
        .slice(
          filter.pageNumber * filter.pageSize,
          (filter.pageNumber + 1) * filter.pageSize
        );
    });

    return {
      totalItemCount,
      filteredEntities,
    };
  }),

  withMethods(store => {
    const superAddToCache = store._addToCache;
    const superUpsertInCache = store._upsertInCache;
    const superRemoveFromCache = store._removeFromCache;

    const updateTotalCount = (entities: SiteCheck[], operation: 'add' | 'subtract') => {
      patchState(store, state => {
        const siteGroups = Object.groupBy(entities, e => e.siteId);
        const siteDeltas = {} as Record<Site['id'], number>;

        for (const [siteIdStr, group] of Object.entries(siteGroups)) {
          const siteId = Number(siteIdStr);
          if (!group) {
            continue;
          }

          let delta = 0;
          if (operation === 'add') {
            const newItems = group.filter(e => !state.ids.includes(e.id));
            delta = newItems.length;
          } else {
            const existingItems = group.filter(e => state.ids.includes(e.id));
            delta = -existingItems.length;
          }

          siteDeltas[siteId] = delta;
        }

        const newTotalCounts = { ...state._totalItemCounts };
        for (const [siteIdStr, delta] of Object.entries(siteDeltas)) {
          const siteId = Number(siteIdStr);
          newTotalCounts[siteId] = (newTotalCounts[siteId] ?? 0) + delta;
        }
        return {
          _totalItemCounts: newTotalCounts
        };
      });
    };

    const _addToCache = (entity: SiteCheck | SiteCheck[], skipUpdateTotalCount = false) => {
      if (!skipUpdateTotalCount) {
        const entities = Array.isArray(entity) ? entity : [entity];
        updateTotalCount(entities, 'add');
      }
      superAddToCache(entity);
    };

    const _upsertInCache = (entity: SiteCheck | SiteCheck[], skipUpdateTotalCount = false) => {
      if (!skipUpdateTotalCount) {
        const entities = Array.isArray(entity) ? entity : [entity];
        updateTotalCount(entities, 'add');
      }
      superUpsertInCache(entity);
    };

    const _removeFromCache = (id: SiteCheck['id'] | SiteCheck['id'][], skipUpdateTotalCount = false) => {
      if (!skipUpdateTotalCount) {
        const ids = Array.isArray(id) ? id : [id];
        const entitiesToRemove = ids
          .map(i => store.entityMap()[i])
        updateTotalCount(entitiesToRemove, 'subtract');
      }
      superRemoveFromCache(id);
    }

    return {
      _addToCache,
      _upsertInCache,
      _removeFromCache,
    }
  }),

  withMethods(store => ({
    deleteSiteCheck: async (siteCheck: SiteCheck) => {
      await store._controller.deleteSiteCheck(siteCheck.siteId, siteCheck.id);
      store._removeFromCache(siteCheck.id);
    },

    deleteAllSiteChecks: async (siteId: number) => {
      await store._controller.deleteAllSiteChecks(siteId);
      const toRemove = store.entities()
        .filter(c => c.siteId === siteId)
        .map(c => c.id);
      store._removeFromCache(toRemove);
    },

    createEmptyCheck: async (siteId: number) => {
      const newSiteCheck = await store._controller.createEmptyCheck(siteId);
      store._upsertInCache([newSiteCheck]);
    },

    upsertFromSite: (site: Site) => {
      // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
      store._upsertInCache(site.siteChecks ?? []);
    },

    loadSiteChecks: (filter: SiteCheckFilter) => {
      patchState(store, state => ({
        _getAllFilter: {
          ...state._getAllFilter,
          ...filter
        }
      }));
    },
  })),

  withHooks(store => ({
    onInit: () => {
      effect(() => {
        if (!store._resource.hasValue()) {
          return;
        }

        const response = store._resource.value();
        if (response == null) {
          return;
        }

        const siteId = store._getAllFilter().siteId;
        if (siteId == null) {
          return;
        }

        patchState(store, state => ({
          _totalItemCounts: {
            ...state._totalItemCounts,
            [siteId]: response.totalItems,
          }
        }));
        store._upsertInCache(response.items, true);
      });
    },
  })),
);
