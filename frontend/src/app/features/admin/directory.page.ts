import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SaakhApi } from '../../core/api/saakh.api';
import {
  AvailabilityStatus,
  CategorySubType,
  DealCategory,
  ProfileRole,
  ProfileSummary,
  VerificationStatus,
} from '../../core/models/domain';
import { BUSINESS_SIZE_LABEL, CATEGORY_META, ROLE_META } from '../../core/models/status-vocabulary';
import { INDIAN_STATES } from '../../core/models/locations';
import { formatDate, formatRange } from '../../core/util/format';
import { ToastService } from '../../core/util/toast.service';
import { EmptyState, LoadingBlock } from '../../shared/ui';
import { StarRating } from '../../shared/star-rating';
import { StatusPill } from '../../shared/status-pill';
import { VerificationBadge } from '../../shared/verification-badge';

/**
 * The user directory: every registered Profile, including the ones discovery
 * hides. Filterable by region, category type, status and profile type, with the
 * filter rigor of a compliance console rather than a consumer search box.
 */
@Component({
  selector: 'sk-directory-page',
  imports: [RouterLink, EmptyState, LoadingBlock, StarRating, StatusPill, VerificationBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './directory.page.html',
  styleUrl: './directory.page.scss',
})
export class DirectoryPage {
  private readonly api = inject(SaakhApi);
  private readonly toast = inject(ToastService);

  readonly ProfileRole = ProfileRole;
  readonly DealCategory = DealCategory;
  readonly VerificationStatus = VerificationStatus;
  readonly AvailabilityStatus = AvailabilityStatus;
  readonly categoryMeta = CATEGORY_META;
  readonly roleMeta = ROLE_META;
  readonly sizeLabel = BUSINESS_SIZE_LABEL;
  readonly states = INDIAN_STATES;
  readonly formatDate = formatDate;

  readonly loading = signal(true);
  readonly rows = signal<ProfileSummary[]>([]);
  readonly total = signal(0);
  readonly taxonomy = signal<CategorySubType[]>([]);

  readonly search = signal('');
  readonly role = signal<ProfileRole | null>(null);
  readonly category = signal<DealCategory | null>(null);
  readonly subTypeId = signal<number | null>(null);
  readonly verification = signal<VerificationStatus | null>(null);
  readonly availability = signal<AvailabilityStatus | null>(null);
  readonly state = signal<string | null>(null);
  readonly district = signal<string | null>(null);
  readonly sortBy = signal<string>('createdAt');
  readonly sortDescending = signal(true);
  readonly page = signal(1);
  readonly pageSize = 25;

  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize)));

  readonly districts = computed(() => {
    const selected = this.state();
    return selected ? (this.states.find((s) => s.name === selected)?.districts ?? []) : [];
  });

  readonly subTypeOptions = computed(() => {
    const category = this.category();
    return category === null
      ? this.taxonomy()
      : this.taxonomy().filter((option) => option.category === category);
  });

  readonly activeFilters = computed(
    () =>
      (this.role() !== null ? 1 : 0) +
      (this.category() !== null ? 1 : 0) +
      (this.subTypeId() !== null ? 1 : 0) +
      (this.verification() !== null ? 1 : 0) +
      (this.availability() !== null ? 1 : 0) +
      (this.state() !== null ? 1 : 0) +
      (this.district() !== null ? 1 : 0) +
      (this.search() ? 1 : 0),
  );

  constructor() {
    this.api.taxonomy().subscribe({ next: (list) => this.taxonomy.set(list) });

    effect(() => {
      const filters = {
        search: this.search() || null,
        role: this.role(),
        category: this.category(),
        categorySubTypeId: this.subTypeId(),
        verificationStatus: this.verification(),
        availabilityStatus: this.availability(),
        state: this.state(),
        district: this.district(),
        sortBy: this.sortBy(),
        sortDescending: this.sortDescending(),
        page: this.page(),
        pageSize: this.pageSize,
      };

      this.loading.set(true);

      this.api.adminDirectory(filters).subscribe({
        next: (result) => {
          this.rows.set(result.items);
          this.total.set(result.total);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.loading.set(false);
          this.toast.error(error);
        },
      });
    });
  }

  capacity(profile: ProfileSummary): string {
    return formatRange(
      profile.capacityMin,
      profile.capacityMax,
      profile.capacityUnit,
      profile.category,
    );
  }

  /** Parses a select's string value back into the enum or null for "any". */
  pick<T extends number>(raw: string, setter: (value: T | null) => void): void {
    setter(raw === '' ? null : (Number(raw) as T));
    this.page.set(1);
  }

  pickText(raw: string, setter: (value: string | null) => void): void {
    setter(raw === '' ? null : raw);
    this.page.set(1);
  }

  onStateChange(raw: string): void {
    this.pickText(raw, (value) => this.state.set(value));
    // A district filter from the previous state would silently exclude everything.
    this.district.set(null);
  }

  onCategoryChange(raw: string): void {
    this.pick<DealCategory>(raw, (value) => this.category.set(value));
    this.subTypeId.set(null);
  }

  sort(column: string): void {
    if (this.sortBy() === column) {
      this.sortDescending.update((value) => !value);
    } else {
      this.sortBy.set(column);
      this.sortDescending.set(true);
    }
    this.page.set(1);
  }

  clear(): void {
    this.search.set('');
    this.role.set(null);
    this.category.set(null);
    this.subTypeId.set(null);
    this.verification.set(null);
    this.availability.set(null);
    this.state.set(null);
    this.district.set(null);
    this.page.set(1);
  }

  changePage(delta: number): void {
    this.page.update((value) => Math.min(this.totalPages(), Math.max(1, value + delta)));
  }
}
