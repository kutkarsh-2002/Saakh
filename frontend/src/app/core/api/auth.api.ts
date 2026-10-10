import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, tap } from 'rxjs';
import {
  AuthResult,
  BusinessSize,
  DealCategory,
  GstinCheckResult,
  ProfileRole,
  Session,
} from '../models/domain';
import { SessionStore } from '../auth/session.store';
import { apiUrl } from './api.config';

export interface RegisterPayload {
  email: string;
  password: string;
  fullName: string;
  role: ProfileRole;
  phone: string;
  gstin: string | null;
  isBusiness: boolean;
  businessSize: BusinessSize;
  country: string;
  state: string;
  district: string;
  category: DealCategory;
  categorySubTypeId: number | null;
  capacityMin: number;
  capacityMax: number;
  capacityUnit: string;
  ownTradeDescription: string | null;
}

@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly http = inject(HttpClient);
  private readonly store = inject(SessionStore);

  /**
   * The email travels with the request because the code may be delivered there
   * rather than by SMS; the form has collected it a step earlier either way.
   */
  /** Real-time registry check, called from the signup form before submission. */
  checkGstin(gstin: string): Observable<GstinCheckResult> {
    return this.http.post<GstinCheckResult>(apiUrl('/api/auth/gstin/check'), { gstin });
  }

  register(payload: RegisterPayload): Observable<AuthResult> {
    return this.http
      .post<AuthResult>(apiUrl('/api/auth/register'), payload)
      .pipe(tap((result) => this.store.apply(result)));
  }

  login(email: string, password: string): Observable<AuthResult> {
    return this.http
      .post<AuthResult>(apiUrl('/api/auth/login'), { email, password })
      .pipe(tap((result) => this.store.apply(result)));
  }

  refresh(refreshToken: string): Observable<AuthResult> {
    return this.http
      .post<AuthResult>(apiUrl('/api/auth/refresh'), { refreshToken })
      .pipe(tap((result) => this.store.apply(result)));
  }

  /** Re-reads the session so tab locks update the moment an admin approves. */
  session(): Observable<Session> {
    return this.http
      .get<Session>(apiUrl('/api/auth/session'))
      .pipe(tap((session) => this.store.setSession(session)));
  }

  logout(): Observable<void> {
    const refreshToken = this.store.refreshToken();
    const request = this.http.post<void>(apiUrl('/api/auth/logout'), { refreshToken });
    return request.pipe(tap({ next: () => this.store.clear(), error: () => this.store.clear() }));
  }
}
