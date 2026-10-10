import { Component, computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl } from '@angular/forms';
import { controlSignal } from './forms';

/**
 * This helper exists because of a bug that shipped: the signup page derived the
 * selected Lender/Seeker side with `computed(() => control.value)`. A computed
 * with no signal dependency is evaluated once and then frozen, so the toggle
 * never moved — while the form underneath held the clicked value. The screen and
 * the submitted data disagreed, which is the worst way for this to fail.
 */
describe('controlSignal', () => {
  @Component({ template: '' })
  class Host {
    readonly control = new FormControl('Maharashtra', { nonNullable: true });
    readonly value = controlSignal(this.control);
    readonly upper = computed(() => this.value().toUpperCase());
  }

  const host = () => {
    TestBed.configureTestingModule({ imports: [Host] });
    return TestBed.createComponent(Host).componentInstance;
  };

  it('starts at the control’s current value, before any change is emitted', () => {
    expect(host().value()).toBe('Maharashtra');
  });

  it('tracks a programmatic setValue, which is how a picker updates a form', () => {
    const component = host();

    component.control.setValue('Gujarat');

    expect(component.value()).toBe('Gujarat');
  });

  it('drives a computed that depends on it', () => {
    const component = host();

    component.control.setValue('Kerala');

    // The real failure mode: the derived value, not the raw one, going stale.
    expect(component.upper()).toBe('KERALA');
  });

  it('keeps tracking across several changes', () => {
    const component = host();

    for (const value of ['Punjab', 'Bihar', 'Odisha']) {
      component.control.setValue(value);
      expect(component.value()).toBe(value);
    }
  });

  it('shows the trap it replaces: a computed over .value never updates', () => {
    const control = new FormControl('before', { nonNullable: true });
    const frozen = computed(() => control.value);

    expect(frozen()).toBe('before');
    control.setValue('after');

    // Documented, not aspirational: this is the behaviour, which is why nothing
    // in the app may read a control this way.
    expect(frozen()).toBe('before');
    expect(control.value).toBe('after');
  });

  it('is not confused by a signal in the same computed', () => {
    // The signup page's subTypes() read a signal *and* a control value, so it
    // recomputed on the signal and looked like it worked — until the signal
    // stopped changing.
    const control = new FormControl(1, { nonNullable: true });
    const other = signal('x');

    TestBed.runInInjectionContext(() => {
      const value = controlSignal(control);
      const combined = computed(() => `${other()}-${value()}`);

      expect(combined()).toBe('x-1');
      control.setValue(2);
      expect(combined()).toBe('x-2');
      other.set('y');
      expect(combined()).toBe('y-2');
    });
  });
});
