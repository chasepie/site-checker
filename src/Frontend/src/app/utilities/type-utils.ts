import { HttpResourceOptions } from '@angular/common/http';
import { Injector, Signal } from '@angular/core';
import { IEntityWithId } from '../generated/model';

export type PartialEntityWithId<T extends IEntityWithId> = Pick<T, 'id'> & Partial<T>;

export function stringIsInteger(value: string): boolean {
  return /^\d+$/.test(value);
}

export function signalIsNonNull<T>(
  signal: Signal<T | null | undefined>
): signal is Signal<T> {
  return signal() != null;
}

export function parseType<T>(obj: unknown, isTypeFunc: (obj: unknown) => obj is T): T {
  if (isTypeFunc(obj)) {
    return obj;
  }
  throw new Error(`Failed to parse type`);
}

export function getHttpResourceOptions<T>(
  isTypeFunc: (obj: unknown) => obj is T,
  injector?: Injector
): HttpResourceOptions<T, unknown> {
  return {
    parse: (obj: unknown) => parseType(obj, isTypeFunc),
    injector
  };
}

export function convertDateToTimeOnlyString(date: Date): string {
  const hours = date.getUTCHours().toString().padStart(2, '0');
  const minutes = date.getUTCMinutes().toString().padStart(2, '0');
  return `${hours}:${minutes}`;
}

export function convertTimeOnlyStringToDate(timeStr: string): Date {
  const [hoursStr, minutesStr] = timeStr.split(':');
  const date = new Date();
  date.setHours(parseInt(hoursStr, 10), parseInt(minutesStr, 10), 0, 0);
  return date;
}
