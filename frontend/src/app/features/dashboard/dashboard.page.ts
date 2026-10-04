import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatDialog } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { SaakhApi } from '../../core/api/saakh.api';
import { SessionStore } from '../../core/auth/session.store';
import {
  CategorySubType,
  DashboardAnalytics,
  DealCategory,
  DealRow,
  LocationOption,
  OpportunityRow,
} from '../../core/models/domain';
import { CATEGORY_META, ROLE_META } from '../../core/models/status-vocabulary';
import { RealtimeService } from '../../core/realtime/realtime.service';
import { formatNumber, formatRange } from '../../core/util/format';
import { ToastService } from '../../core/util/toast.service';
import { DealTable } from '../../shared/deal-table';
import { EmptyState, FilterChip, LoadingBlock, PageHeader, StatTile } from '../../shared/ui';
import { StatusPill } from '../../shared/status-pill';
import { TrustSummaryView } from '../../shared/trust-summary';
import { VerificationBadge } from '../../shared/verification-badge';
import { AnalyticsCharts } from './analytics-charts';
import { DealActions } from '../deals/deal-actions';
import { SendInterestDialog, SendInterestDialogData } from './send-interest.dialog';

/**
 * The Opportunity dashboard, laid out exactly as the spec specifies, top to
 * bottom: the two analytics graphs, the filter bar, the opportunity table, and
 * then the user's own open deals as a separate table.
 *
 * The view is symmetric by perspective: a Lender sees Seekers to evaluate and a
 * Seeker sees Lenders, driven off the session's role rather than two screens.
 */
@Component({
  selector: 'sk-dashboard-page',
  imports: [
    FormsModule,
    RouterLink,
    MatTooltipModule,
    PageHeader,
    StatTile,
    FilterChip,
    EmptyState,
    LoadingBlock,
    StatusPill,
    TrustSummaryView,
    VerificationBadge,
    DealTable,
    AnalyticsCharts,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard.page.html',
  styleUrl: './dashboard.page.scss',
})
export class DashboardPage {
  private readonly api = inject(SaakhApi);
  private readonly store = inject(SessionStore);
  private readonly realtime = inject(RealtimeService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly dealActions = inject(DealActions);

  readonly DealCategory = DealCategory;
  readonly categoryMeta = CATEGORY_META;
  readonly roleMeta = ROLE_META;
  readonly formatNumber = formatNumber;

  readonly profile = this.store.profile;

  readonly counterpartyLabel = computed(() => {
    const role = this.profile()?.role;
    return role === undefined ? 'counterparties' : ROLE_META[role].discovers.toLowerCase();
  });

  // ---- data ----------------------------------------------------------------
  readonly loadingOpportunities = signal(true);
  readonly loadingDeals = signal(true);
  readonly analytics = signal<DashboardAnalytics | null>(null);
  readonly opportunities = signal<OpportunityRow[]>([]);
  readonly totalOpportunities = signal(0);
  readonly openDeals = signal<DealRow[]>([]);
  readonly taxonomy = signal<CategorySubType[]>([]);
  readonly locations = signal<LocationOption[]>([]);

  // ---- filters -------------------------------------------------------------
  readonly search = signal('');
  readonly selectedStates = signal<string[]>([]);
  readonly selectedDistricts = signal<string[]>([]);
  readonly selectedCategories = signal<DealCategory[]>([]);
  readonly selectedSubTypes = signal<number[]>([]);
  readonly capacityMin = signal<number | null>(null);
  readonly capacityMax = signal<number | null>(null);
  readonly minRating = signal<number | null>(null);
  readonly sortBy = signal('match');
  readonly page = signal(1);
  readonly pageSize = 10;
  readonly filtersOpen = signal(false);

  readonly activeFilterCount = computed(
    () =>
      this.selectedStates().length +
      this.selectedDistricts().length +
      this.selectedCategories().length +
      this.selectedSubTypes().length +
      (this.capacityMin() !== null ? 1 : 0) +
      (this.capacityMax() !== null ? 1 : 0) +
      (this.minRating() !== null ? 1 : 0),
  );

  readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalOpportunities() / this.pageSize)),
  );

  /** Districts offered depend on which states are selected, if any. */
  readonly districtOptions = computed(() => {
    const states = this.selectedStates();
    const source = states.length
      ? this.locations().filter((location) => states.includes(location.state))
      : this.locations();

    return [...new Set(source.flatMap((location) => location.districts))].sort();
  });

  readonly subTypeOptions = computed(() => {
    const categories = this.selectedCategories();
    return categories.length
      ? this.taxonomy().filter((subType) => categories.includes(subType.category))
      : this.taxonomy();
  });

  readonly settlementSoon = computed(
    () =>
      this.openDeals().filter((deal) => {
        const days = Math.ceil(
          (new Date(deal.estimatedSettlementTime).getTime() - Date.now()) / 86400000,
        );
        return days <= 7;
      }).length,
  );

  constructor() {
    this.api.taxonomy().subscribe({ next: (list) => this.taxonomy.set(list) });
    this.api.locationOptions().subscribe({ next: (list) => this.locations.set(list) });
    this.loadAnalytics();
    this.loadOpenDeals();

    // Re-run discovery whenever any filter signal changes.
    effect(() => {
      const filters = {
        search: this.search() || null,
        states: this.selectedStates(),
        districts: this.selectedDistricts(),
        categories: this.selectedCategories(),
        subTypeIds: this.selectedSubTypes(),
        capacityMin: this.capacityMin(),
        capacityMax: this.capacityMax(),
        minRating: this.minRating(),
        sortBy: this.sortBy(),
        page: this.page(),
        pageSize: this.pageSize,
      };

      this.loadingOpportunities.set(true);

      this.api.opportunities(filters).subscribe({
        next: (result) => {
          this.opportunities.set(result.items);
          this.totalOpportunities.set(result.total);
          this.loadingOpportunities.set(false);
        },
        error: (error: unknown) => {
          this.loadingOpportunities.set(false);
          this.toast.error(error);
        },
      });
    });

    // A deal changing state elsewhere should move on this dashboard too.
    this.realtime.dealState$.subscribe(() => {
      this.loadOpenDeals();
      this.loadAnalytics();
    });

    this.realtime.interest$.subscribe(() => this.page.set(this.page()));
  }

  private loadAnalytics(): void {
    this.api.analytics().subscribe({
      next: (data) => this.analytics.set(data),
      error: () => undefined,
    });
  }

  private loadOpenDeals(): void {
    this.loadingDeals.set(true);
    this.api.openDeals().subscribe({
      next: (rows) => {
        this.openDeals.set(rows);
        this.loadingDeals.set(false);
      },
      error: (error: unknown) => {
        this.loadingDeals.set(false);
        this.toast.error(error);
      },
    });
  }

  // ---- filter handlers -----------------------------------------------------

  private toggleIn<T>(list: () => T[], set: (value: T[]) => void, value: T): void {
    const current = list();
    set(current.includes(value) ? current.filter((item) => item !== value) : [...current, value]);
    this.page.set(1);
  }

  toggleState(state: string): void {
    this.toggleIn(this.selectedStates, (value) => this.selectedStates.set(value), state);
    // Dropping a state should not leave its districts filtering invisibly.
    const allowed = this.districtOptions();
    this.selectedDistricts.update((districts) =>
      districts.filter((district) => allowed.includes(district)),
    );
  }

  toggleDistrict(district: string): void {
    this.toggleIn(this.selectedDistricts, (value) => this.selectedDistricts.set(value), district);
  }

  toggleCategory(category: DealCategory): void {
    this.toggleIn(this.selectedCategories, (value) => this.selectedCategories.set(value), category);
    const allowed = this.subTypeOptions().map((option) => option.id);
    this.selectedSubTypes.update((ids) => ids.filter((id) => allowed.includes(id)));
  }

  toggleSubType(id: number): void {
    this.toggleIn(this.selectedSubTypes, (value) => this.selectedSubTypes.set(value), id);
  }

  setMinRating(value: number | null): void {
    this.minRating.set(this.minRating() === value ? null : value);
    this.page.set(1);
  }

  applyCapacity(min: string, max: string): void {
    this.capacityMin.set(min === '' ? null : Number(min));
    this.capacityMax.set(max === '' ? null : Number(max));
    this.page.set(1);
  }

  clearFilters(): void {
    this.selectedStates.set([]);
    this.selectedDistricts.set([]);
    this.selectedCategories.set([]);
    this.selectedSubTypes.set([]);
    this.capacityMin.set(null);
    this.capacityMax.set(null);
    this.minRating.set(null);
    this.search.set('');
    this.page.set(1);
  }

  changePage(delta: number): void {
    this.page.update((value) => Math.min(this.totalPages(), Math.max(1, value + delta)));
  }

  // ---- row actions ---------------------------------------------------------

  capacityLabel(row: OpportunityRow): string {
    return formatRange(
      row.profile.capacityMin,
      row.profile.capacityMax,
      row.profile.capacityUnit,
      row.profile.category,
    );
  }

  sendInterest(row: OpportunityRow): void {
    const data: SendInterestDialogData = { profile: row.profile };

    this.dialog
      .open(SendInterestDialog, { data, width: '520px', maxWidth: '94vw' })
      .afterClosed()
      .subscribe((note: string | null | undefined) => {
        if (note === undefined) {
          return;
        }

        this.api.sendInterest(row.profile.id, note).subscribe({
          next: () => {
            this.toast.success(
              `Interest sent to ${row.profile.name}. Chat opens once they accept.`,
            );
            // Re-run discovery so the row reflects its new state.
            this.page.set(this.page());
          },
          error: (error: unknown) => this.toast.error(error),
        });
      });
  }

  openInterest(row: OpportunityRow): void {
    if (row.interestId) {
      void this.router.navigate(['/interests', row.interestId]);
    }
  }

  rateDeal(deal: DealRow): void {
    this.dealActions.rate(deal, () => {
      this.loadOpenDeals();
      this.loadAnalytics();
    });
  }

  requestResume(deal: DealRow): void {
    this.dealActions.requestResume(deal, () => this.loadOpenDeals());
  }

  acceptResume(deal: DealRow): void {
    this.dealActions.acceptResume(deal, () => {
      this.loadOpenDeals();
      this.loadAnalytics();
    });
  }
}
