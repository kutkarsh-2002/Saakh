import { Injectable, computed, signal } from '@angular/core';
import {
  AuthResult,
  AvailabilityStatus,
  ProfileRole,
  Session,
  VerificationStatus,
} from '../models/domain';

const TOKEN_KEY = 'saakh.tokens';

interface StoredTokens {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: string;
}

/**
 * The single place that knows who is signed in. Route guards, the shell and the
 * verification banner all read from here, so the tab-locking rule is decided
 * once rather than re-derived per screen.
 */
@Injectable({ providedIn: 'root' })
export class SessionStore {
  private readonly _session = signal<Session | null>(null);
  private readonly _tokens = signal<StoredTokens | null>(readStoredTokens());

  readonly session = this._session.asReadonly();

  readonly accessToken = computed(() => this._tokens()?.accessToken ?? null);
  readonly refreshToken = computed(() => this._tokens()?.refreshToken ?? null);

  /** A stored token is enough to attempt a session restore on a cold load. */
  readonly hasToken = computed(() => !!this._tokens());

  readonly profile = computed(() => this._session()?.profile ?? null);
  readonly admin = computed(() => this._session()?.admin ?? null);

  readonly isAdmin = computed(() => !!this._session()?.admin);
  readonly isTrader = computed(() => !!this._session()?.profile);

  readonly role = computed<ProfileRole | null>(() => this.profile()?.role ?? null);

  readonly verificationStatus = computed<VerificationStatus | null>(
    () => this.profile()?.verificationStatus ?? null,
  );

  /**
   * The access gate from the spec: while an account is Needs Approval, Pending
   * or Rejected, every tab except its own Profile stays hidden.
   */
  readonly featuresUnlocked = computed(
    () => this.profile()?.verificationStatus === VerificationStatus.Active,
  );

  readonly isSuspended = computed(
    () => this.profile()?.availabilityStatus === AvailabilityStatus.Suspended,
  );

  readonly isRemoved = computed(
    () => this.profile()?.availabilityStatus === AvailabilityStatus.Removed,
  );

  /** What this profile discovers: a Lender sees Seekers, a Seeker sees Lenders. */
  readonly counterpartyRole = computed<ProfileRole | null>(() => {
    const role = this.role();
    if (role === null) {
      return null;
    }
    return role === ProfileRole.Lender ? ProfileRole.Seeker : ProfileRole.Lender;
  });

  readonly displayName = computed(
    () => this.profile()?.name ?? this.admin()?.name ?? this._session()?.fullName ?? '',
  );

  apply(result: AuthResult): void {
    this.setTokens({
      accessToken: result.accessToken,
      refreshToken: result.refreshToken,
      accessTokenExpiresAt: result.accessTokenExpiresAt,
    });
    this._session.set(result.session);
  }

  setSession(session: Session | null): void {
    this._session.set(session);
  }

  setTokens(tokens: StoredTokens | null): void {
    this._tokens.set(tokens);
    try {
      if (tokens) {
        localStorage.setItem(TOKEN_KEY, JSON.stringify(tokens));
      } else {
        localStorage.removeItem(TOKEN_KEY);
      }
    } catch {
      // Private browsing or blocked site data: the session still works for this
      // tab, it just will not survive a reload.
    }
  }

  clear(): void {
    this._session.set(null);
    this.setTokens(null);
  }
}

function readStoredTokens(): StoredTokens | null {
  try {
    const raw = localStorage.getItem(TOKEN_KEY);
    return raw ? (JSON.parse(raw) as StoredTokens) : null;
  } catch {
    return null;
  }
}
