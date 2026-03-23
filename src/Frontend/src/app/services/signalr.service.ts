import { Injectable } from '@angular/core';
import { HubConnectionBuilder } from '@microsoft/signalr';
import { Observable } from 'rxjs';
import { ZodType } from 'zod';
import {
  CreatedEntityChange, DeletedEntityChange,
  PiaLocation,
  SignalRConstants,
  UpdatedEntityChange
} from '../generated/model';

@Injectable({
  providedIn: 'root'
})
export class SignalrService {
  private readonly _connection = new HubConnectionBuilder()
    .withUrl(`/${SignalRConstants.HubName}`)
    .withAutomaticReconnect()
    .build();

  private getObservable<T>(zodType: ZodType<T>, methodName: string) {
    return new Observable<T>(sub => {
      const handler = (data: unknown) => {
        const result = zodType.safeParse(data);
        if (result.success) {
          sub.next(result.data);
        }
      };

      this._connection.on(methodName, handler);
      return () => {
        this._connection.off(methodName, handler);
      };
    });
  }

  public readonly entityAdded$ = this.getObservable(CreatedEntityChange, SignalRConstants.OnEntityCreatedKey);
  public readonly entityUpdated$ = this.getObservable(UpdatedEntityChange, SignalRConstants.OnEntityUpdatedKey);
  public readonly entityDeleted$ = this.getObservable(DeletedEntityChange, SignalRConstants.OnEntityDeletedKey);
  public readonly locationChanged$ = this.getObservable(PiaLocation, SignalRConstants.OnLocationChangedKey);

  public get connectionId() {
    return this._connection.connectionId;
  }

  public async init() {
    await this._connection.start();
  }
}
