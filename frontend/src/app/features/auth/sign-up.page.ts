import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
} from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthApi } from '../../core/api/auth.api';
import { SaakhApi } from '../../core/api/saakh.api';
import { SessionStore } from '../../core/auth/session.store';
import {
  GSTIN_PATTERN,
  BusinessSize,
  CategorySubType,
  DealCategory,
  GstinCheckResult,
  ProfileRole,
} from '../../core/models/domain';
import { ROLE_META } from '../../core/models/status-vocabulary';
import { INDIAN_STATES } from '../../core/models/locations';
import { controlSignal } from '../../core/util/forms';
import { messageFor } from '../../core/util/toast.service';
import { AuthLayout } from './auth-layout';

/**
 * The GSTIN format pattern, mirrored from the backend so the field gives instant
 * feedback and a mistyped number never reaches the registry (and never spends a
 * provider credit).
 */

@Component({
  selector: 'sk-sign-up-page',
  imports: [ReactiveFormsModule, RouterLink, AuthLayout],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sign-up.page.html',
  styleUrl: './sign-up.page.scss',
})
export class SignUpPage {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthApi);
  private readonly api = inject(SaakhApi);
  private readonly store = inject(SessionStore);
  private readonly router = inject(Router);

  readonly ProfileRole = ProfileRole;
  readonly DealCategory = DealCategory;
  readonly BusinessSize = BusinessSize;
  readonly roleMeta = ROLE_META;
  readonly states = INDIAN_STATES;

  readonly step = signal(1);
  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);

  readonly taxonomy = signal<CategorySubType[]>([]);

  // ---- GSTIN ---------------------------------------------------------------
  readonly gstinChecking = signal(false);
  readonly gstinResult = signal<GstinCheckResult | null>(null);

  readonly identity = this.fb.nonNullable.group({
    role: [ProfileRole.Seeker, Validators.required],
    fullName: ['', [Validators.required, Validators.maxLength(200)]],
    isBusiness: [false],
    businessSize: [BusinessSize.Individual],
    email: ['', [Validators.required, Validators.email]],
    phone: ['', [Validators.required, Validators.pattern(/^\d{10}$/)]],
    password: ['', [Validators.required, Validators.minLength(8)]],
  });

  readonly verification = this.fb.nonNullable.group({
    gstin: [''],
  });

  readonly trade = this.fb.nonNullable.group({
    state: ['', Validators.required],
    district: ['', Validators.required],
    category: [DealCategory.RawMaterial, Validators.required],
    categorySubTypeId: [null as number | null, Validators.required],
    capacityMin: [0, [Validators.required, Validators.min(0)]],
    capacityMax: [0, [Validators.required, Validators.min(0)]],
    ownTradeDescription: [''],
  });

  // Anything the screen derives from a form control reads it as a signal; see
  // `controlSignal` for why reading `.value` inside a computed silently freezes.
  readonly selectedRole = controlSignal(this.identity.controls.role);
  private readonly selectedState = controlSignal(this.trade.controls.state);
  private readonly selectedCategory = controlSignal(this.trade.controls.category);
  private readonly selectedSubTypeId = controlSignal(this.trade.controls.categorySubTypeId);

  readonly districts = computed(() => {
    const state = this.selectedState();
    return this.states.find((s) => s.name === state)?.districts ?? [];
  });

  readonly subTypes = computed(() =>
    this.taxonomy().filter((s) => s.category === this.selectedCategory()),
  );

  readonly capacityUnit = computed(() => {
    const id = this.selectedSubTypeId();
    return this.taxonomy().find((s) => s.id === id)?.defaultUnit ?? 'INR';
  });

  /**
   * The verification path this signup is on. Stated before the user submits, so
   * nobody is surprised by a locked account after creating one.
   */
  readonly path = computed<'gstin' | 'review'>(() => {
    const result = this.gstinResult();
    return result?.isValid ? 'gstin' : 'review';
  });

  constructor() {
    this.api.taxonomy().subscribe({
      next: (list) => {
        this.taxonomy.set(list);
        this.applyDefaultSubType();
      },
      error: () => undefined,
    });

    this.trade.controls.category.valueChanges.subscribe(() => this.applyDefaultSubType());
    this.trade.controls.state.valueChanges.subscribe(() =>
      this.trade.controls.district.setValue(''),
    );

    this.identity.controls.isBusiness.valueChanges.subscribe((isBusiness) => {
      this.identity.controls.businessSize.setValue(
        isBusiness ? BusinessSize.Small : BusinessSize.Individual,
      );
    });

    // A Seeker states the trade they are in, for a counterparty's context.
    this.identity.controls.role.valueChanges.subscribe((role) => {
      const control = this.trade.controls.ownTradeDescription;
      if (role === ProfileRole.Seeker) {
        control.addValidators(Validators.required);
      } else {
        control.removeValidators(Validators.required);
      }
      control.updateValueAndValidity();
    });

    // Re-typing the GSTIN invalidates whatever the registry said about the old one.
    this.verification.controls.gstin.valueChanges.subscribe(() => this.gstinResult.set(null));

  }

  private applyDefaultSubType(): void {
    const options = this.subTypes();
    const current = this.trade.controls.categorySubTypeId.value;

    if (!options.some((option) => option.id === current)) {
      this.trade.controls.categorySubTypeId.setValue(options[0]?.id ?? null);
    }
  }

  invalidIn(group: 'identity' | 'verification' | 'trade', control: string): boolean {
    // Cast to the base type: the three groups have different shapes, so the
    // union of their typed `get` signatures is not callable as one.
    const form = this[group] as unknown as { get(path: string): AbstractControl | null };
    const field = form.get(control);
    return !!field && field.invalid && (field.dirty || field.touched);
  }

  // ---- step 1 --------------------------------------------------------------

  pickRole(role: ProfileRole): void {
    this.identity.controls.role.setValue(role);
  }

  // ---- step 2: GSTIN -------------------------------------------------------

  get gstinFormatOk(): boolean {
    const value = this.verification.controls.gstin.value.trim().toUpperCase();
    return GSTIN_PATTERN.test(value);
  }

  get gstinEntered(): boolean {
    return this.verification.controls.gstin.value.trim().length > 0;
  }

  checkGstin(): void {
    const value = this.verification.controls.gstin.value.trim().toUpperCase();

    if (!GSTIN_PATTERN.test(value)) {
      return;
    }

    this.gstinChecking.set(true);
    this.error.set(null);

    this.auth.checkGstin(value).subscribe({
      next: (result) => {
        this.gstinChecking.set(false);
        this.gstinResult.set(result);
      },
      error: (error: unknown) => {
        this.gstinChecking.set(false);
        this.gstinResult.set({
          isValid: false,
          legalName: null,
          status: null,
          message: messageFor(error, 'We could not verify that GSTIN.'),
          retrying: false,
        });
      },
    });
  }

  clearGstin(): void {
    this.verification.controls.gstin.setValue('');
    this.gstinResult.set(null);
  }

  // ---- navigation ----------------------------------------------------------

  next(): void {
    this.error.set(null);

    if (this.step() === 1) {
      if (this.identity.invalid) {
        this.identity.markAllAsTouched();
        return;
      }
      this.step.set(2);
      return;
    }

    if (this.step() === 2) {
      // A GSTIN that was typed but not accepted blocks the step, because an
      // invalid GSTIN blocks profile creation until it is corrected.
      if (this.gstinEntered && !this.gstinResult()?.isValid) {
        this.error.set(
          'That GSTIN has not been verified. Check it, or clear the field to continue without one.',
        );
        return;
      }

      this.step.set(3);
    }
  }

  back(): void {
    this.error.set(null);
    this.step.update((value) => Math.max(1, value - 1));
  }

  submit(): void {
    if (this.trade.invalid) {
      this.trade.markAllAsTouched();
      return;
    }

    const capacityMin = Number(this.trade.controls.capacityMin.value);
    const capacityMax = Number(this.trade.controls.capacityMax.value);

    if (capacityMax < capacityMin) {
      this.error.set('The upper end of your range cannot be below the lower end.');
      return;
    }

    this.submitting.set(true);
    this.error.set(null);

    const identity = this.identity.getRawValue();
    const verification = this.verification.getRawValue();
    const trade = this.trade.getRawValue();

    this.auth
      .register({
        email: identity.email.trim(),
        password: identity.password,
        fullName: identity.fullName.trim(),
        role: identity.role,
        phone: identity.phone.trim(),
        gstin: verification.gstin.trim() ? verification.gstin.trim().toUpperCase() : null,
        isBusiness: identity.isBusiness,
        businessSize: identity.businessSize,
        country: 'India',
        state: trade.state,
        district: trade.district,
        category: trade.category,
        categorySubTypeId: trade.categorySubTypeId,
        capacityMin,
        capacityMax,
        capacityUnit: this.capacityUnit(),
        ownTradeDescription: trade.ownTradeDescription.trim() || null,
      })
      .subscribe({
        next: () => {
          this.submitting.set(false);
          // A GSTIN-verified account lands on the dashboard; a no-GSTIN account
          // lands on its Profile, which is the only tab it can open.
          void this.router.navigate([this.store.featuresUnlocked() ? '/dashboard' : '/profile']);
        },
        error: (error: unknown) => {
          this.submitting.set(false);
          this.error.set(messageFor(error, 'We could not create your account.'));
        },
      });
  }
}
