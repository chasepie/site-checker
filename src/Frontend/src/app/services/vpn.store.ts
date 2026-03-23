import { inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  patchState, signalStore, withHooks,
  withMethods, withProps, withState
} from '@ngrx/signals';
import { updateEntity, upsertEntities, withEntities } from '@ngrx/signals/entities';
import { tap } from 'rxjs';
import { PiaLocation, VpnController } from '../generated/model';
import { SignalrService } from './signalr.service';

export const VpnStore = signalStore(
  { providedIn: 'root' },
  withEntities<PiaLocation>(),

  withState({
    currentLocation: null as PiaLocation | null,
  }),

  withProps(() => ({
    _controller: inject(VpnController),
    _signalrService: inject(SignalrService),
  })),

  withMethods(store => ({
    _updateCurrentLocation(location: PiaLocation) {
      patchState(store, () => ({
        currentLocation: location,
      }));
    },

    _upsertLocations(locations: PiaLocation | PiaLocation[]) {
      const entities = Array.isArray(locations) ? locations : [locations];
      patchState(store, upsertEntities(entities));
    },

    _excludeLocation(id: string) {
      patchState(store, updateEntity({
        id, changes: () => ({ excluded: true }),
      }));
    },

    async changeLocation(excludeCurrent = false) {
      const currentLocId = store.currentLocation()?.id;
      if (excludeCurrent && currentLocId) {
        patchState(store, updateEntity({
          id: currentLocId,
          changes: () => ({ excluded: true }),
        }));
      }

      const nextLoc = await store._controller.changeLocation(excludeCurrent);

      patchState(store, () => ({
        currentLocation: nextLoc,
      }));
    }
  })),

  withHooks({
    onInit: (store) => {
      store._signalrService.locationChanged$
        .pipe(
          takeUntilDestroyed(),
          tap(nextLoc => {
            store._updateCurrentLocation(nextLoc);
          }),
        )
        .subscribe();

      void store._controller.getCurrentLocation()
        .then(currLoc => { store._updateCurrentLocation(currLoc); });

      void store._controller.getAllLocations()
        .then(allLocs => { store._upsertLocations(allLocs); });
    },
  })
);
