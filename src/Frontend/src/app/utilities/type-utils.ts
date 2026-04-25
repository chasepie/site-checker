import { IEntityWithId } from '../generated/model';

export type PartialEntityWithId<T extends IEntityWithId> = Pick<T, 'id'> & Partial<T>;

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
