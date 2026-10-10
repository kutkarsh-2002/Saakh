import { EvidenceViewer } from '../../core/util/evidence-viewer';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SaakhApi } from '../../core/api/saakh.api';
import { AdminOverview } from '../../core/models/domain';
import { RealtimeService } from '../../core/realtime/realtime.service';

/**
 * The Admin console shell.
 *
 * Built as an ops tool, not a consumer screen: the queue depth is the first
 * thing on the page, the counters are dense and tabular, and the default route
 * is the verification queue rather than a summary dashboard, because an admin
 * opens this to triage, not to browse.
 */
@Component({
  selector: 'sk-admin-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  // Shared by the queue and the profile screens beneath this shell.
  providers: [EvidenceViewer],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sk-page">
      <header class="head">
        <div>
          <p class="sk-eyebrow">Moderation</p>
          <h1>Admin console</h1>
          <p class="sk-lede">
            Verification decisions and moderation actions are logged against your account with a
            timestamp.
          </p>
        </div>
      </header>

      @if (overview(); as counts) {
        <!-- Queue depth first: it is the number an admin is here to reduce. -->
        <div class="counters">
          <a class="counter counter--urgent" routerLink="/admin/queue">
            <span class="counter__label">Awaiting review</span>
            <span class="counter__value sk-figure">{{ counts.pendingReview }}</span>
            <span class="counter__hint">Evidence submitted</span>
          </a>
          <a class="counter" routerLink="/admin/queue">
            <span class="counter__label">Needs approval</span>
            <span class="counter__value sk-figure">{{ counts.needsApproval }}</span>
            <span class="counter__hint">Nothing submitted yet</span>
          </a>
          <a class="counter" routerLink="/admin/queue">
            <span class="counter__label">Rejected</span>
            <span class="counter__value sk-figure">{{ counts.rejected }}</span>
            <span class="counter__hint">Can resubmit</span>
          </a>
          <a class="counter" routerLink="/admin/directory">
            <span class="counter__label">Active profiles</span>
            <span class="counter__value sk-figure">{{ counts.activeProfiles }}</span>
            <span class="counter__hint">of {{ counts.totalProfiles }} total</span>
          </a>
          <a class="counter" routerLink="/admin/directory">
            <span class="counter__label">Suspended</span>
            <span class="counter__value sk-figure">{{ counts.suspended }}</span>
            <span class="counter__hint">{{ counts.removed }} removed</span>
          </a>
          <span class="counter counter--static">
            <span class="counter__label">Deals open</span>
            <span class="counter__value sk-figure">{{ counts.openDeals }}</span>
            <span class="counter__hint">
              {{ counts.completedDeals }} settled · {{ counts.haltedDeals }} halted
            </span>
          </span>
        </div>
      }

      <nav class="tabs" aria-label="Console sections">
        <a routerLink="/admin/queue" routerLinkActive="tabs__link--on" class="tabs__link">
          <span class="material-symbols-rounded" aria-hidden="true">fact_check</span>
          Verification queue
          @if (overview(); as counts) {
            @if (counts.pendingReview > 0) {
              <span class="tabs__badge sk-figure">{{ counts.pendingReview }}</span>
            }
          }
        </a>
        <a routerLink="/admin/directory" routerLinkActive="tabs__link--on" class="tabs__link">
          <span class="material-symbols-rounded" aria-hidden="true">groups</span>
          User directory
        </a>
        <a routerLink="/admin/activity" routerLinkActive="tabs__link--on" class="tabs__link">
          <span class="material-symbols-rounded" aria-hidden="true">receipt_long</span>
          Action log
        </a>
      </nav>

      <router-outlet />
    </div>
  `,
  styleUrl: './admin.shell.scss',
})
export class AdminShell {
  private readonly api = inject(SaakhApi);
  private readonly realtime = inject(RealtimeService);

  readonly overview = signal<AdminOverview | null>(null);

  constructor() {
    this.load();
    // A new submission should bump the queue depth without a manual refresh.
    this.realtime.verification$.subscribe(() => this.load());
  }

  private load(): void {
    this.api.adminOverview().subscribe({
      next: (counts) => this.overview.set(counts),
      error: () => undefined,
    });
  }
}
