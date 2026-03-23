import { effect, inject, resource } from '@angular/core';
import { patchState, signalStore, withHooks, withMethods, withProps, withState } from '@ngrx/signals';
import { addEntity, withEntities } from '@ngrx/signals/entities';
import { SiteCheck, SiteCheckController, SiteCheckScreenshot } from '../generated/model';

export const ScreenshotStore = signalStore(
  { providedIn: 'root' },
  withEntities<SiteCheckScreenshot>(),
  withState({
    _filter: null as SiteCheck | null,
  }),
  withProps(() => ({
    _controller: inject(SiteCheckController),
  })),
  withProps(store => ({
    _resource: resource({
      params: () => ({ siteCheck: store._filter() }),
      loader: ({ params }) => {
        const sc = params.siteCheck;
        if (!sc) {
          return Promise.resolve(null);
        }

        const screenshot = store.entities()
          .find(c => c.siteCheckId === sc.id);
        if (screenshot) {
          return Promise.resolve(screenshot);
        }

        return store._controller.getScreenshot(sc.siteId, sc.id);
      },
    })
  })),

  withMethods(store => ({
    loadScreenshot(siteCheck: SiteCheck) {
      patchState(store, () => ({
        _filter: siteCheck
      }));
    },
    _addToCache(screenshot: SiteCheckScreenshot) {
      patchState(store, addEntity(screenshot));
    }
  })),

  withHooks({
    onInit: (store) => {
      effect(() => {
        const resource = store._resource;
        if (!resource.hasValue()) {
          return;
        }

        const response = resource.value();
        if (response != null) {
          store._addToCache(response);
        }
      })
    },
  }),
);
