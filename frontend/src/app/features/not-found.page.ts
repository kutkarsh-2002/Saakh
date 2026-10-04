import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SessionStore } from '../core/auth/session.store';

@Component({
  selector: 'sk-not-found-page',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sk-page">
      <div class="sk-card sk-card-pad wrap">
        <span class="material-symbols-rounded wrap__icon" aria-hidden="true">explore_off</span>
        <h1>That page does not exist</h1>
        <p class="sk-lede">
          The link may be out of date, or the deal may belong to two other parties.
        </p>
        <a class="sk-btn sk-btn--primary" [routerLink]="home">Back to {{ homeLabel }}</a>
      </div>
    </div>
  `,
  styles: [
    `
      .wrap {
        display: grid;
        justify-items: center;
        gap: var(--sk-space-3);
        text-align: center;
        max-width: 34rem;
        margin: var(--sk-space-7) auto;
      }

      .wrap__icon {
        font-size: 3rem;
        color: var(--sk-line-strong);
      }
    `,
  ],
})
export class NotFoundPage {
  private readonly store = inject(SessionStore);

  readonly home = this.store.isAdmin()
    ? '/admin'
    : this.store.featuresUnlocked()
      ? '/dashboard'
      : '/profile';

  readonly homeLabel = this.store.isAdmin()
    ? 'the console'
    : this.store.featuresUnlocked()
      ? 'Opportunity'
      : 'your profile';
}
