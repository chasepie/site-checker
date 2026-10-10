import { Injectable } from '@angular/core';
import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { Observable } from 'rxjs';
import { ZodType } from 'zod';
import {
  CreatedEntityChange, DeletedEntityChange,
  PiaLocation,
  SignalRConstants,
  TestRunResult,
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
        } else {
          console.warn(`Ignored a ${methodName} message that doesn't match its schema.`, result.error, data);
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
  /** Sent only to the connection that started the Test Run. */
  public readonly testRunCompleted$ = this.getObservable(TestRunResult, SignalRConstants.OnTestRunCompletedKey);

  public get connectionId() {
    return this._connection.connectionId;
  }

  /** Connects, unless already connected or connecting. The hub requires a login. */
  public async start() {
    if (this._connection.state === HubConnectionState.Disconnected) {
      await this._connection.start();
    }
  }

  public async stop() {
    await this._connection.stop();
  }

  /**
   * Called when the connection closes for good, after automatic reconnecting gives up. The server
   * closes it when the session expires.
   */
  public onClose(callback: (error?: Error) => void) {
    this._connection.onclose(callback);
  }

  /** Called when the connection drops and automatic reconnecting starts, such as after a logout. */
  public onReconnecting(callback: (error?: Error) => void) {
    this._connection.onreconnecting(callback);
  }
}
