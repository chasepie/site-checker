import { computed, Directive, input, Signal } from '@angular/core';
import {
  createMetadataKey, FieldTree, metadata,
  SchemaPathTree
} from '@angular/forms/signals';

const VALIDATED = createMetadataKey<boolean>();

@Directive({
  selector: '[appFormValid]',
  host: {
    '[class.is-invalid]': 'this._isValidated() && this._fieldState().invalid()',
    '[class.is-valid]': 'this._isValidated() && this._fieldState().valid()',
  }
})
export class FormValid<T, U extends string | number> {
  public readonly formField = input.required<FieldTree<T, U>>();

  protected readonly _fieldState = computed(() => this.formField()());

  protected readonly _isValidated = computed(() => {
    const metaIsValidated = this._fieldState().metadata(VALIDATED);
    if (metaIsValidated != null) {
      return metaIsValidated() ?? false;
    }

    return false;
  });
}

type PathType<T> = SchemaPathTree<T>
  & Record<keyof T, SchemaPathTree<unknown>>;

export function setValidatedMetadata<T extends object>(
  path: PathType<T>,
  baseObject: T,
  isValidated: Signal<boolean>
) {
  const keys = Object.keys(baseObject) as (keyof T)[];
  for (const key of keys) {
    const value = baseObject[key];

    if (typeof value === 'object'
      && !Array.isArray(value)
      && value != null
    ) {
      const subPath = path[key] as PathType<typeof value>;
      setValidatedMetadata(subPath, value, isValidated);

    } else {
      metadata(path[key], VALIDATED, () => isValidated());
    }
  }
}
