import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthApi } from '../../core/api/auth.api';
import { SessionStore } from '../../core/auth/session.store';
import { messageFor } from '../../core/util/toast.service';
import { AuthLayout } from './auth-layout';

/** The seeded demo logins, so a reviewer can get in without reading the README. */
const DEMO_ACCOUNTS = [
  { label: 'Lender', email: 'lender1@saakh.demo', note: 'Settled history, deals in flight' },
  { label: 'Seeker', email: 'seeker1@saakh.demo', note: 'Settled history, deals in flight' },
  { label: 'Needs approval', email: 'needsapproval@saakh.demo', note: 'No GSTIN, tabs locked' },
  { label: 'Admin', email: 'admin@saakh.app', note: 'Verification queue and directory' },
];

@Component({
  selector: 'sk-sign-in-page',
  imports: [ReactiveFormsModule, RouterLink, AuthLayout],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <sk-auth-layout
      title="Sign in"
      subtitle="Pick up where you left off: your opportunities, deals and trust record."
    >
      <form class="sk-card sk-card-pad form" [formGroup]="form" (ngSubmit)="submit()">
        @if (error()) {
          <p class="alert" role="alert">
            <span class="material-symbols-rounded" aria-hidden="true">error</span>
            <span>{{ error() }}</span>
          </p>
        }

        <div class="sk-field">
          <label for="email">Email address</label>
          <input
            id="email"
            type="email"
            class="sk-input"
            formControlName="email"
            autocomplete="email"
            inputmode="email"
            [class.is-invalid]="invalid('email')"
          />
          @if (invalid('email')) {
            <p class="sk-error">
              <span class="material-symbols-rounded" aria-hidden="true">error</span>
              Enter the email address you signed up with.
            </p>
          }
        </div>

        <div class="sk-field">
          <label for="password">Password</label>
          <div class="password">
            <input
              id="password"
              [type]="showPassword() ? 'text' : 'password'"
              class="sk-input"
              formControlName="password"
              autocomplete="current-password"
              [class.is-invalid]="invalid('password')"
            />
            <button
              type="button"
              class="password__toggle"
              (click)="showPassword.set(!showPassword())"
              [attr.aria-label]="showPassword() ? 'Hide password' : 'Show password'"
            >
              <span class="material-symbols-rounded" aria-hidden="true">
                {{ showPassword() ? 'visibility_off' : 'visibility' }}
              </span>
            </button>
          </div>
          @if (invalid('password')) {
            <p class="sk-error">
              <span class="material-symbols-rounded" aria-hidden="true">error</span>
              Enter your password.
            </p>
          }
        </div>

        <button
          type="submit"
          class="sk-btn sk-btn--primary sk-btn--block"
          [disabled]="submitting()"
        >
          @if (submitting()) {
            <span class="material-symbols-rounded spin" aria-hidden="true">progress_activity</span>
            Signing in…
          } @else {
            Sign in
          }
        </button>

        <p class="switch">
          New to Saakh?
          <a routerLink="/sign-up">Create your profile</a>
        </p>
      </form>

      <section class="demo sk-card">
        <div class="demo__head">
          <p class="sk-eyebrow">Demo accounts</p>
          <p class="demo__hint">
            Seeded with <code>dotnet run --seed</code>. Password for all of them:
            <code>Saakh&#64;2026</code>
          </p>
        </div>

        <div class="demo__list">
          @for (account of demoAccounts; track account.email) {
            <button type="button" class="demo__item" (click)="useDemo(account.email)">
              <span class="demo__label">{{ account.label }}</span>
              <span class="demo__email">{{ account.email }}</span>
              <span class="demo__note">{{ account.note }}</span>
            </button>
          }
        </div>
      </section>
    </sk-auth-layout>
  `,
  styles: [
    `
      .form {
        display: grid;
        gap: var(--sk-space-4);
      }

      .alert {
        display: flex;
        gap: var(--sk-space-2);
        margin: 0;
        padding: var(--sk-space-3);
        border-radius: var(--sk-radius);
        background: var(--sk-halted-soft);
        color: var(--sk-halted-soft-ink);
        box-shadow: inset 0 0 0 1.5px var(--sk-halted);
        font-size: 0.875rem;
        font-weight: 500;
      }

      .alert .material-symbols-rounded {
        font-size: 1.25rem;
        flex: none;
      }

      .password {
        position: relative;
        display: flex;
      }

      .password .sk-input {
        padding-right: 48px;
      }

      .password__toggle {
        position: absolute;
        right: 2px;
        top: 50%;
        transform: translateY(-50%);
        display: grid;
        place-items: center;
        width: 40px;
        height: 40px;
        border: 0;
        border-radius: var(--sk-radius-sm);
        background: transparent;
        color: var(--sk-ink-muted);
        cursor: pointer;
      }

      .password__toggle:hover {
        background: var(--sk-surface-sunken);
        color: var(--sk-ink);
      }

      .switch {
        margin: 0;
        text-align: center;
        font-size: 0.9375rem;
        color: var(--sk-ink-muted);
      }

      .spin {
        animation: spin 900ms linear infinite;
      }

      @keyframes spin {
        to {
          transform: rotate(360deg);
        }
      }

      .demo {
        margin-top: var(--sk-space-4);
        overflow: hidden;
      }

      .demo__head {
        padding: var(--sk-space-3) var(--sk-space-4);
        border-bottom: 1px solid var(--sk-line-hairline);
        background: var(--sk-surface-sunken);
      }

      .demo__hint {
        margin: 2px 0 0;
        font-size: 0.8125rem;
        color: var(--sk-ink-muted);
      }

      .demo__hint code {
        font-size: 0.8125rem;
        padding: 1px 4px;
        border-radius: 4px;
        background: var(--sk-surface);
        border: 1px solid var(--sk-line);
      }

      .demo__list {
        display: grid;
      }

      .demo__item {
        display: grid;
        grid-template-columns: 7.5rem 1fr;
        grid-template-areas: 'label email' 'label note';
        gap: 0 var(--sk-space-3);
        align-items: center;
        width: 100%;
        min-height: var(--sk-touch);
        padding: var(--sk-space-2) var(--sk-space-4);
        border: 0;
        border-bottom: 1px solid var(--sk-line-hairline);
        background: transparent;
        text-align: left;
        cursor: pointer;
      }

      .demo__item:last-child {
        border-bottom: 0;
      }

      .demo__item:hover {
        background: var(--sk-brand-soft);
      }

      .demo__label {
        grid-area: label;
        font-size: 0.75rem;
        font-weight: 700;
        letter-spacing: 0.04em;
        text-transform: uppercase;
        color: var(--sk-brand);
      }

      .demo__email {
        grid-area: email;
        font-size: 0.875rem;
        font-weight: 600;
        color: var(--sk-ink);
      }

      .demo__note {
        grid-area: note;
        font-size: 0.75rem;
        color: var(--sk-ink-muted);
      }
    `,
  ],
})
export class SignInPage {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthApi);
  private readonly store = inject(SessionStore);
  private readonly router = inject(Router);

  readonly demoAccounts = DEMO_ACCOUNTS;

  readonly submitting = signal(false);
  readonly showPassword = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  invalid(control: 'email' | 'password'): boolean {
    const field = this.form.controls[control];
    return field.invalid && (field.dirty || field.touched);
  }

  useDemo(email: string): void {
    this.form.patchValue({ email, password: 'Saakh@2026' });
    this.error.set(null);
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.error.set(null);

    const { email, password } = this.form.getRawValue();

    this.auth.login(email, password).subscribe({
      next: () => {
        this.submitting.set(false);
        // Admins have no dashboard; an unverified trader has only their Profile.
        if (this.store.isAdmin()) {
          void this.router.navigate(['/admin']);
        } else if (this.store.featuresUnlocked()) {
          void this.router.navigate(['/dashboard']);
        } else {
          void this.router.navigate(['/profile']);
        }
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.error.set(messageFor(error, 'We could not sign you in. Try again.'));
      },
    });
  }
}
