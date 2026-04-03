import { inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
    patchState, signalStore, withHooks,
    withMethods, withProps, withState
} from '@ngrx/signals';
import { updateEntity, upsertEntities, withEntities } from '@ngrx/signals/entities';
import { tap } from 'rxjs';
import { VpnController, VpnLocation } from '../generated/model';
import { SignalrService } from './signalr.service';

// Extends VpnLocation with client-side-only excluded tracking (not from API)
export type VpnLocationEntry = VpnLocation & { excluded: boolean };

function toEntry(location: VpnLocation): VpnLocationEntry {
  return { ...location, excluded: false };
}

export const VpnStore = signalStore(
  { providedIn: 'root' },
  withEntities<VpnLocationEntry>(),

  withState({
    currentLocation: null as VpnLocation | null,
  }),

  withProps(() => ({
    _controller: inject(VpnController),
    _signalrService: inject(SignalrService),
  })),

  withMethods(store => ({
    _updateCurrentLocation(location: VpnLocation) {
      patchState(store, () => ({
        currentLocation: location,
      }));
    },

    _upsertLocations(locations: VpnLocation | VpnLocation[]) {
      const entries = Array.isArray(locations) ? locations.map(toEntry) : [toEntry(locations)];
      patchState(store, upsertEntities(entries));
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
