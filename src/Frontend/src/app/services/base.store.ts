import { inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { patchState, signalStoreFeature, withHooks, withMethods, withProps } from '@ngrx/signals';
import {
  addEntities,
  removeEntities,
  updateEntity, upsertEntity,
  withEntities
} from '@ngrx/signals/entities';
import { filter, map, Observable } from 'rxjs';
import { ZodType } from 'zod';
import { CreatedEntityChange, DeletedEntityChange, EntityChange, IEntityWithId, UpdatedEntityChange } from '../generated/model';
import { PartialEntityWithId } from '../utilities/type-utils';
import { SignalrService } from './signalr.service';

export function withCrudEntities<T extends IEntityWithId>(
  entityTypeName: EntityChange['entityTypeName'],
  zodObject: ZodType<T>
) {
  return signalStoreFeature(
    withEntities<T>(),

    withProps(() => ({
      _signalrService: inject(SignalrService),
    })),

    withMethods((store) => ({
      _addToCache(entity: T | T[]) {
        const entities = Array.isArray(entity) ? entity : [entity];
        patchState(store, addEntities(entities));
      },

      _updateInCache(entity: PartialEntityWithId<T> | PartialEntityWithId<T>[]) {
        const entities = Array.isArray(entity) ? entity : [entity];
        entities.forEach(e => { patchState(store, updateEntity({ id: e.id, changes: e })); });
      },

      _upsertInCache(entity: T | T[]) {
        const entities = Array.isArray(entity) ? entity : [entity];
        entities.forEach(e => { patchState(store, upsertEntity(e)); });
      },

      _removeFromCache(id: T['id'] | T['id'][]) {
        const ids = Array.isArray(id) ? id : [id];
        patchState(store, removeEntities(ids))
      },
    })),

    withHooks({
      onInit(store) {
        type CUDEntityChange = CreatedEntityChange | UpdatedEntityChange | DeletedEntityChange;
        const hasTypeName = <T extends CUDEntityChange>(obs$: Observable<T>) =>
          obs$.pipe(
            takeUntilDestroyed(),
            filter((change) => change.entityTypeName === entityTypeName),
          );

        const isOfType = <T extends CreatedEntityChange | UpdatedEntityChange>(obs$: Observable<T>) =>
          obs$.pipe(
            map((change) => {
              const e = 'entity' in change ? change.entity : change.newEntity;
              return zodObject.safeParse(e);
            }),
            filter((result) => result.success),
          );

        store._signalrService.entityAdded$
          .pipe(hasTypeName, isOfType)
          .subscribe((result) => {
            store._upsertInCache(result.data);
          });

        store._signalrService.entityUpdated$
          .pipe(hasTypeName, isOfType)
          .subscribe((result) => {
            store._upsertInCache(result.data);
          });

        store._signalrService.entityDeleted$
          .pipe(hasTypeName)
          .subscribe((change) => {
            store._removeFromCache(change.entityId);
          });
      },
    })
  )
};
