import { Signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl } from '@angular/forms';

/**
 * Reads a reactive-form control as a signal.
 *
 * A `computed()` that reads `control.value` directly looks like it works and does
 * not: `value` is a plain property, so the computed has no signal to depend on,
 * computes once and is then frozen for the life of the component. It fails
 * silently — the form holds the right value while the screen keeps showing the
 * first one — which is the worst way for this to go wrong. Every derived value
 * that depends on a control goes through here.
 *
 * Must be called in an injection context, which a field initializer is.
 */
export function controlSignal<T>(control: AbstractControl<T>): Signal<T> {
  return toSignal(control.valueChanges, { initialValue: control.value });
}
