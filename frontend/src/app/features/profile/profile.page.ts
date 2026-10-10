import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatTooltipModule } from '@angular/material/tooltip';
import { SaakhApi } from '../../core/api/saakh.api';
import { AuthApi } from '../../core/api/auth.api';
import { SessionStore } from '../../core/auth/session.store';
import {
  GSTIN_PATTERN,
  AvailabilityStatus,
  BusinessSize,
  CategorySubType,
  DealCategory,
  EvidenceDecision,
  ProfileRole,
  ProfileSummary,
  VerificationState,
  VerificationStatus,
} from '../../core/models/domain';
import { BUSINESS_SIZE_LABEL, CATEGORY_META, ROLE_META } from '../../core/models/status-vocabulary';
import { INDIAN_STATES } from '../../core/models/locations';
import { RealtimeService } from '../../core/realtime/realtime.service';
import { formatDate, formatDateTime, formatRange } from '../../core/util/format';
import { controlSignal } from '../../core/util/forms';
import { ToastService } from '../../core/util/toast.service';
import { PageHeader } from '../../shared/ui';
import { StatusPill } from '../../shared/status-pill';
import { TrustSummaryView } from '../../shared/trust-summary';
import { VerificationBadge } from '../../shared/verification-badge';
import { VerificationBanner } from '../../shared/verification-banner';

/** What an admin will accept as proof. The spec leaves this open, so the list is
 *  stated here as a concrete, reviewable set rather than left vague for users. */
/**
 * What a reviewer is actually being asked to look at, in three groups rather than a
 * list of specific papers. A vendor who holds something that proves the same thing by
 * a different name was previously stuck choosing the nearest wrong option.
 */
const EVIDENCE_TYPES = [
  'Legal document',
  'Tax document',
  'Operational document',
];

/** What each group covers, shown under the picker so the choice is obvious. */
export const EVIDENCE_TYPE_HINTS: Record<string, string> = {
  'Legal document': 'Shop or trade licence, registration certificate, rental agreement for the premises.',
  'Tax document': 'PAN card, GST registration certificate, a recent tax filing.',
  'Operational document':
    'Electricity or utility bill, bank statement showing the business name, a supplier invoice.',
};

/**
 * Profile & verification: the one tab an unverified account can open.
 *
 * It carries both verification paths. A GSTIN-verified profile sees its
 * registry confirmation; a no-GSTIN profile sees the banner for whichever of
 * the four states it is in, plus the evidence upload that moves it forward.
 */
@Component({
  selector: 'sk-profile-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatTooltipModule,
    PageHeader,
    StatusPill,
    TrustSummaryView,
    VerificationBadge,
    VerificationBanner,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './profile.page.html',
  styleUrl: './profile.page.scss',
})
export class ProfilePage {
  private readonly api = inject(SaakhApi);
  private readonly auth = inject(AuthApi);
  private readonly store = inject(SessionStore);
  private readonly fb = inject(FormBuilder);
  private readonly toast = inject(ToastService);
  private readonly realtime = inject(RealtimeService);
  private readonly route = inject(ActivatedRoute);

  readonly VerificationStatus = VerificationStatus;
  readonly AvailabilityStatus = AvailabilityStatus;
  readonly EvidenceDecision = EvidenceDecision;
  readonly DealCategory = DealCategory;
  readonly ProfileRole = ProfileRole;
  readonly BusinessSize = BusinessSize;
  readonly categoryMeta = CATEGORY_META;
  readonly roleMeta = ROLE_META;
  readonly sizeLabel = BUSINESS_SIZE_LABEL;
  readonly states = INDIAN_STATES;
  readonly evidenceTypes = EVIDENCE_TYPES;
  readonly formatDate = formatDate;
  readonly formatDateTime = formatDateTime;

  readonly profile = signal<ProfileSummary | null>(null);
  readonly verification = signal<VerificationState | null>(null);
  readonly taxonomy = signal<CategorySubType[]>([]);

  readonly editing = signal(false);
  readonly saving = signal(false);
  readonly uploading = signal(false);

  readonly selectedFiles = signal<File[]>([]);
  readonly documentType = signal(EVIDENCE_TYPES[0]);

  readonly documentTypeHint = computed(() => EVIDENCE_TYPE_HINTS[this.documentType()] ?? '');

  /**
   * Whether this profile can actually be found. Both gates have to be open, so an
   * unapproved account reads as Inactive however its availability flag is stored —
   * the toggle shows where the account stands, not what one of two fields says.
   */
  readonly visibleInSearch = computed(() => {
    const me = this.profile();
    return (
      !!me &&
      me.verificationStatus === VerificationStatus.Active &&
      me.availabilityStatus === AvailabilityStatus.Active
    );
  });

  /**
   * Adding a GSTIN after signup. Kept outside the edit form: this is not an edit to
   * a detail, it is the one route that can open a locked account without a reviewer.
   */
  readonly gstinInput = signal('');
  readonly gstinTouched = signal(false);
  readonly verifyingGstin = signal(false);

  readonly gstinWellFormed = computed(() => GSTIN_PATTERN.test(this.gstinInput()));

  /** Set when the user was bounced here by the verified guard from a locked tab. */
  readonly blockedFrom = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    isBusiness: [false],
    businessSize: [BusinessSize.Individual],
    state: ['', Validators.required],
    district: ['', Validators.required],
    category: [DealCategory.RawMaterial, Validators.required],
    categorySubTypeId: [null as number | null, Validators.required],
    capacityMin: [0, [Validators.required, Validators.min(0)]],
    capacityMax: [0, [Validators.required, Validators.min(0)]],
    ownTradeDescription: [''],
  });

  // Read as signals, not as `.value` inside a computed — see `controlSignal`.
  private readonly selectedState = controlSignal(this.form.controls.state);
  private readonly selectedCategory = controlSignal(this.form.controls.category);
  private readonly selectedSubTypeId = controlSignal(this.form.controls.categorySubTypeId);

  readonly districts = computed(() => {
    const state = this.selectedState();
    return this.states.find((option) => option.name === state)?.districts ?? [];
  });

  readonly subTypes = computed(() =>
    this.taxonomy().filter((option) => option.category === this.selectedCategory()),
  );

  readonly capacityUnit = computed(() => {
    const id = this.selectedSubTypeId();
    return this.taxonomy().find((option) => option.id === id)?.defaultUnit ?? 'INR';
  });

  readonly capacityLabel = computed(() => {
    const profile = this.profile();
    if (!profile) {
      return '';
    }
    return formatRange(
      profile.capacityMin,
      profile.capacityMax,
      profile.capacityUnit,
      profile.category,
    );
  });

  readonly awaitingReview = computed(
    () =>
      this.verification()?.evidence.filter(
        (document) => document.decision === EvidenceDecision.AwaitingReview,
      ) ?? [],
  );

  constructor() {
    this.blockedFrom.set(this.route.snapshot.queryParamMap.get('locked'));

    this.api.taxonomy().subscribe({ next: (list) => this.taxonomy.set(list) });
    this.load();

    // An admin decision arriving over the socket refreshes the banner in place.
    this.realtime.verification$.subscribe(() => this.load());
  }

  private load(): void {
    this.api.myProfile().subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.resetForm(profile);
      },
      error: (error: unknown) => this.toast.error(error),
    });

    this.api.verificationState().subscribe({
      next: (state) => this.verification.set(state),
      error: () => undefined,
    });
  }

  onGstinInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value.toUpperCase().replace(/\s/g, '');
    this.gstinInput.set(value);
    this.gstinTouched.set(true);
  }

  addGstin(): void {
    if (!this.gstinWellFormed() || this.verifyingGstin()) {
      return;
    }

    this.verifyingGstin.set(true);

    this.api.addGstin(this.gstinInput()).subscribe({
      next: (result) => {
        this.verifyingGstin.set(false);

        if (result.verified) {
          this.gstinInput.set('');
          this.gstinTouched.set(false);
          this.toast.success(`${result.message} Your account is open.`);
          // Re-read the session so the shell unlocks the tabs straight away rather
          // than on the next navigation.
          this.auth.session().subscribe({ error: () => undefined });
        } else {
          // The registry was down. The number is saved and retried, so say so
          // rather than leaving the field looking like it failed.
          this.toast.info(result.message);
        }

        this.load();
      },
      error: (error: unknown) => {
        this.verifyingGstin.set(false);
        this.toast.error(error);
      },
    });
  }

  private resetForm(profile: ProfileSummary): void {
    this.form.reset({
      name: profile.name,
      isBusiness: profile.isBusiness,
      businessSize: profile.businessSize,
      state: profile.state,
      district: profile.district,
      category: profile.category,
      categorySubTypeId: profile.subType?.id ?? null,
      capacityMin: profile.capacityMin,
      capacityMax: profile.capacityMax,
      ownTradeDescription: profile.ownTradeDescription ?? '',
    });
  }

  invalid(control: string): boolean {
    const field = this.form.get(control);
    return !!field && field.invalid && (field.dirty || field.touched);
  }

  startEdit(): void {
    const profile = this.profile();
    if (profile) {
      this.resetForm(profile);
    }
    this.editing.set(true);
  }

  cancelEdit(): void {
    const profile = this.profile();
    if (profile) {
      this.resetForm(profile);
    }
    this.editing.set(false);
  }

  onCategoryChange(): void {
    const options = this.subTypes();
    const current = this.form.controls.categorySubTypeId.value;
    if (!options.some((option) => option.id === current)) {
      this.form.controls.categorySubTypeId.setValue(options[0]?.id ?? null);
    }
  }

  onStateChange(): void {
    this.form.controls.district.setValue('');
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();

    if (Number(value.capacityMax) < Number(value.capacityMin)) {
      this.toast.error(null, 'The upper end of your capacity range cannot be below the lower end.');
      return;
    }

    this.saving.set(true);

    this.api
      .updateProfile({
        name: value.name.trim(),
        isBusiness: value.isBusiness,
        businessSize: value.businessSize,
        country: 'India',
        state: value.state,
        district: value.district,
        category: value.category,
        categorySubTypeId: value.categorySubTypeId,
        capacityMin: Number(value.capacityMin),
        capacityMax: Number(value.capacityMax),
        capacityUnit: this.capacityUnit(),
        ownTradeDescription: value.ownTradeDescription.trim() || null,
      })
      .subscribe({
        next: (updated) => {
          this.saving.set(false);
          this.editing.set(false);
          this.profile.set(updated);
          this.syncSession(updated);
          this.toast.success('Your profile is updated.');
        },
        error: (error: unknown) => {
          this.saving.set(false);
          this.toast.error(error);
        },
      });
  }

  private syncSession(profile: ProfileSummary): void {
    const session = this.store.session();
    if (session) {
      this.store.setSession({ ...session, profile });
    }
  }

  // ---- evidence ------------------------------------------------------------

  onFilesPicked(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFiles.set(Array.from(input.files ?? []));
  }

  removeFile(index: number): void {
    this.selectedFiles.update((files) => files.filter((_, i) => i !== index));
  }

  fileSize(bytes: number): string {
    if (bytes < 1024) {
      return `${bytes} B`;
    }
    if (bytes < 1024 * 1024) {
      return `${Math.round(bytes / 1024)} KB`;
    }
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  submitEvidence(): void {
    const files = this.selectedFiles();

    if (files.length === 0) {
      this.toast.error(null, 'Attach at least one document first.');
      return;
    }

    this.uploading.set(true);

    this.api.submitEvidence(files, this.documentType()).subscribe({
      next: (state) => {
        this.uploading.set(false);
        this.verification.set(state);
        this.selectedFiles.set([]);
        // Verification status changed, so the session's tab locks have to be
        // re-read from the server rather than guessed at here.
        this.auth.session().subscribe({ next: () => undefined, error: () => undefined });
        this.api.myProfile().subscribe({ next: (profile) => this.profile.set(profile) });
        this.toast.success(
          'Documents submitted. An administrator will review them and you will be notified here.',
        );
      },
      error: (error: unknown) => {
        this.uploading.set(false);
        this.toast.error(error);
      },
    });
  }

  scrollToEvidence(): void {
    document.getElementById('evidence')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  // ---- availability --------------------------------------------------------

  setAvailability(active: boolean): void {
    this.api.setAvailability(active).subscribe({
      next: (updated) => {
        this.profile.set(updated);
        this.syncSession(updated);
        this.toast.success(
          active
            ? 'You are active and visible in search again.'
            : 'You are inactive and hidden from search. Your in-flight deals carry on.',
        );
      },
      error: (error: unknown) => this.toast.error(error),
    });
  }
}
