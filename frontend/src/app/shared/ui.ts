import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

/**
 * Small shared primitives: page header, stat tile, filter chip, empty state and
 * a loading block. Defined once here so no screen reinvents them.
 */

@Component({
  selector: 'sk-page-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="head">
      <div class="head__text">
        @if (eyebrow()) {
          <p class="sk-eyebrow">{{ eyebrow() }}</p>
        }
        <h1>{{ title() }}</h1>
        @if (subtitle()) {
          <p class="sk-lede">{{ subtitle() }}</p>
        }
      </div>
      <div class="head__actions">
        <ng-content />
      </div>
    </header>
  `,
  styles: [
    `
      .head {
        display: flex;
        align-items: flex-end;
        justify-content: space-between;
        gap: var(--sk-space-4);
        flex-wrap: wrap;
        margin-bottom: var(--sk-space-5);
      }

      .head__text {
        display: grid;
        gap: 2px;
        min-width: 0;
      }

      .head__actions {
        display: flex;
        align-items: center;
        gap: var(--sk-space-2);
        flex-wrap: wrap;
      }
    `,
  ],
})
export class PageHeader {
  readonly eyebrow = input<string | null>(null);
  readonly title = input.required<string>();
  readonly subtitle = input<string | null>(null);
}

/**
 * A single figure with its label. Numbers are set in tabular figures at a size
 * that survives a glance in sunlight, which is the whole point of the tile.
 */
@Component({
  selector: 'sk-stat-tile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="tile" [style.--tile-color]="'var(--sk-' + tone() + ')'">
      <div class="tile__top">
        @if (icon()) {
          <span class="material-symbols-rounded tile__icon" aria-hidden="true">{{ icon() }}</span>
        }
        <p class="tile__label">{{ label() }}</p>
      </div>
      <p class="tile__value sk-figure">{{ value() }}</p>
      @if (hint()) {
        <p class="tile__hint">{{ hint() }}</p>
      }
    </div>
  `,
  styles: [
    `
      :host {
        display: block;
      }

      .tile {
        height: 100%;
        padding: var(--sk-space-3) var(--sk-space-4);
        background: var(--sk-surface);
        border: 1px solid var(--sk-line);
        border-radius: var(--sk-radius-lg);
        /* A top rule in the tone colour, not a left border: keeps the tiles
           reading as one row of figures rather than a stack of alert cards. */
        box-shadow: inset 0 3px 0 0 var(--tile-color), var(--sk-shadow-sm);
      }

      .tile__top {
        display: flex;
        align-items: center;
        gap: 6px;
        margin-top: 2px;
      }

      .tile__icon {
        font-size: 1rem;
        color: var(--tile-color);
      }

      .tile__label {
        margin: 0;
        font-size: 0.6875rem;
        font-weight: 700;
        letter-spacing: 0.06em;
        text-transform: uppercase;
        color: var(--sk-ink-muted);
      }

      .tile__value {
        margin: 2px 0 0;
        font-size: 1.75rem;
        line-height: 1.15;
        color: var(--sk-ink-strong);
      }

      .tile__hint {
        margin: 2px 0 0;
        font-size: 0.8125rem;
        color: var(--sk-ink-muted);
      }
    `,
  ],
})
export class StatTile {
  readonly label = input.required<string>();
  readonly value = input.required<string | number>();
  readonly hint = input<string | null>(null);
  readonly icon = input<string | null>(null);

  /** Token suffix for the accent rule, e.g. 'brand', 'open', 'halted'. */
  readonly tone = input('brand');
}

/** A multi-select filter chip. A real button with aria-pressed, not a styled div. */
@Component({
  selector: 'sk-filter-chip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      type="button"
      class="chip"
      [class.chip--on]="selected()"
      [attr.aria-pressed]="selected()"
      (click)="toggled.emit()"
    >
      @if (icon()) {
        <span class="material-symbols-rounded chip__icon" aria-hidden="true">{{ icon() }}</span>
      }
      <span>{{ label() }}</span>
      @if (selected()) {
        <span class="material-symbols-rounded chip__check" aria-hidden="true">check</span>
      }
    </button>
  `,
  styles: [
    `
      :host {
        display: inline-flex;
      }

      .chip {
        display: inline-flex;
        align-items: center;
        gap: 5px;
        /* 36px rather than 44px: chips sit in dense rows, and each one also has
           a 44px-tall tap area via the padding on its row container. */
        min-height: 36px;
        padding: 0 var(--sk-space-3);
        border-radius: var(--sk-radius-pill);
        border: 1px solid var(--sk-line-strong);
        background: var(--sk-surface);
        color: var(--sk-ink);
        font-family: var(--sk-font-ui);
        font-size: 0.8125rem;
        font-weight: 600;
        cursor: pointer;
        transition: background-color 120ms ease, border-color 120ms ease, color 120ms ease;
      }

      .chip:hover {
        background: var(--sk-surface-sunken);
        border-color: var(--sk-ink-subtle);
      }

      .chip--on {
        background: var(--sk-brand);
        border-color: var(--sk-brand);
        color: var(--sk-ink-inverse);
      }

      .chip--on:hover {
        background: var(--sk-brand-hover);
        border-color: var(--sk-brand-hover);
      }

      .chip__icon,
      .chip__check {
        font-size: 1rem;
      }
    `,
  ],
})
export class FilterChip {
  readonly label = input.required<string>();
  readonly selected = input(false);
  readonly icon = input<string | null>(null);
  readonly toggled = output<void>();
}

/**
 * The empty state. It says what would fill the space and what to do about it,
 * rather than only reporting that there is nothing here.
 */
@Component({
  selector: 'sk-empty-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="empty">
      <span class="material-symbols-rounded empty__icon" aria-hidden="true">{{ icon() }}</span>
      <p class="empty__title">{{ title() }}</p>
      @if (message()) {
        <p class="empty__message">{{ message() }}</p>
      }
      <ng-content />
    </div>
  `,
  styles: [
    `
      .empty {
        display: grid;
        justify-items: center;
        gap: var(--sk-space-2);
        padding: var(--sk-space-7) var(--sk-space-4);
        text-align: center;
      }

      .empty__icon {
        font-size: 2.5rem;
        color: var(--sk-line-strong);
      }

      .empty__title {
        margin: 0;
        font-family: var(--sk-font-display);
        font-size: 1.125rem;
        font-weight: 600;
        color: var(--sk-ink);
      }

      .empty__message {
        margin: 0;
        max-width: 42ch;
        font-size: 0.9375rem;
        color: var(--sk-ink-muted);
      }
    `,
  ],
})
export class EmptyState {
  readonly icon = input('inbox');
  readonly title = input.required<string>();
  readonly message = input<string | null>(null);
}

/** Skeleton block, so a slow connection shows structure rather than a blank page. */
@Component({
  selector: 'sk-loading',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="loading" role="status" [attr.aria-label]="label()">
      @for (row of rows(); track $index) {
        <span class="loading__bar"></span>
      }
    </div>
  `,
  styles: [
    `
      .loading {
        display: grid;
        gap: var(--sk-space-2);
        padding: var(--sk-space-4);
      }

      .loading__bar {
        height: 14px;
        border-radius: var(--sk-radius-sm);
        background: linear-gradient(
          90deg,
          var(--sk-surface-sunken) 25%,
          var(--sk-line-hairline) 37%,
          var(--sk-surface-sunken) 63%
        );
        background-size: 400% 100%;
        animation: loading-shimmer 1.4s ease infinite;
      }

      .loading__bar:nth-child(even) {
        width: 80%;
      }

      @keyframes loading-shimmer {
        from {
          background-position: 100% 50%;
        }
        to {
          background-position: 0 50%;
        }
      }
    `,
  ],
})
export class LoadingBlock {
  readonly label = input('Loading');
  readonly count = input(4);

  rows(): number[] {
    return Array.from({ length: this.count() }, (_, index) => index);
  }
}
