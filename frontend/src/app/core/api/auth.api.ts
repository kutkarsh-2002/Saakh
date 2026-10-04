import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, tap } from 'rxjs';
import {
  AuthResult,
  BusinessSize,
  DealCategory,
  GstinCheckResult,
  OtpRequestResult,
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
  otpCode: string;
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

  requestOtp(phone: string): Observable<OtpRequestResult> {
    return this.http.post<OtpRequestResult>(apiUrl('/api/auth/otp/request'), { phone });
  }

  verifyOtp(phone: string, code: string): Observable<{ verified: boolean }> {
    return this.http.post<{ verified: boolean }>(apiUrl('/api/auth/otp/verify'), { phone, code });
  }

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
