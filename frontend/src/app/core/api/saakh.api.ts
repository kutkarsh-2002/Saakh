import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  DealProposal,
  ProposalThread,
  AddGstinResult,
  AdminActionLog,
  AdminActionType,
  AdminOverview,
  AdminProfileDetail,
  AppNotification,
  AvailabilityStatus,
  BusinessSize,
  CategorySubType,
  DashboardAnalytics,
  DealCategory,
  DealDetail,
  DealRow,
  DealState,
  Interest,
  LocationOption,
  Message,
  OpportunityRow,
  PagedResult,
  ProfileRole,
  ProfileSummary,
  Rating,
  ResumeRequest,
  SuspensionDuration,
  VerificationQueueRow,
  VerificationState,
  VerificationStatus,
} from '../models/domain';
import { apiUrl } from './api.config';

export interface DiscoveryFilters {
  search?: string | null;
  country?: string | null;
  states?: string[];
  districts?: string[];
  categories?: DealCategory[];
  subTypeIds?: number[];
  capacityMin?: number | null;
  capacityMax?: number | null;
  minRating?: number | null;
  sortBy?: string;
  page?: number;
  pageSize?: number;
}

export interface RaiseTicketPayload {
  interestId: string;
  category: DealCategory;
  categorySubTypeId: number | null;
  capacity: number;
  capacityUnit: string;
  materialDescription: string | null;
  description: string;
  estimatedSettlementTime: string;
}

export interface UpdateProfilePayload {
  name: string;
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

export interface AdminDirectoryFilters {
  search?: string | null;
  role?: ProfileRole | null;
  category?: DealCategory | null;
  categorySubTypeId?: number | null;
  verificationStatus?: VerificationStatus | null;
  availabilityStatus?: AvailabilityStatus | null;
  state?: string | null;
  district?: string | null;
  page?: number;
  pageSize?: number;
  sortBy?: string | null;
  sortDescending?: boolean;
}

/** Every product endpoint in one service: the app's state is shallow enough that
 *  Angular services plus RxJS carry it without a store layer. */
@Injectable({ providedIn: 'root' })
export class SaakhApi {
  private readonly http = inject(HttpClient);

  // ---- taxonomy and profile ------------------------------------------------

  taxonomy(): Observable<CategorySubType[]> {
    return this.http.get<CategorySubType[]>(apiUrl('/api/profiles/taxonomy'));
  }

  myProfile(): Observable<ProfileSummary> {
    return this.http.get<ProfileSummary>(apiUrl('/api/profiles/me'));
  }

  updateProfile(payload: UpdateProfilePayload): Observable<ProfileSummary> {
    return this.http.put<ProfileSummary>(apiUrl('/api/profiles/me'), payload);
  }

  setAvailability(active: boolean): Observable<ProfileSummary> {
    return this.http.put<ProfileSummary>(apiUrl('/api/profiles/me/availability'), { active });
  }

  /**
   * Adds a GSTIN to an account that signed up without one. The server verifies it
   * against the registry and opens the account on the spot when it checks out.
   */
  addGstin(gstin: string): Observable<AddGstinResult> {
    return this.http.post<AddGstinResult>(apiUrl('/api/profiles/me/gstin'), { gstin });
  }

  verificationState(): Observable<VerificationState> {
    return this.http.get<VerificationState>(apiUrl('/api/profiles/me/verification'));
  }

  submitEvidence(files: File[], documentType: string): Observable<VerificationState> {
    const form = new FormData();
    for (const file of files) {
      form.append('files', file, file.name);
    }
    form.append('documentType', documentType);
    return this.http.post<VerificationState>(
      apiUrl('/api/profiles/me/verification/evidence'),
      form,
    );
  }

  profile(id: string): Observable<ProfileSummary> {
    return this.http.get<ProfileSummary>(apiUrl(`/api/profiles/${id}`));
  }

  profileRatings(id: string): Observable<Rating[]> {
    return this.http.get<Rating[]>(apiUrl(`/api/profiles/${id}/ratings`));
  }

  // ---- discovery and dashboard ---------------------------------------------

  opportunities(filters: DiscoveryFilters): Observable<PagedResult<OpportunityRow>> {
    let params = new HttpParams();

    const append = (key: string, value: unknown) => {
      if (value !== null && value !== undefined && value !== '') {
        params = params.append(key, String(value));
      }
    };

    append('search', filters.search);
    append('country', filters.country);
    append('capacityMin', filters.capacityMin);
    append('capacityMax', filters.capacityMax);
    append('minRating', filters.minRating);
    append('sortBy', filters.sortBy);
    append('page', filters.page);
    append('pageSize', filters.pageSize);

    // Multi-select filters repeat the key, which is what the API binds from.
    for (const state of filters.states ?? []) {
      params = params.append('states', state);
    }
    for (const district of filters.districts ?? []) {
      params = params.append('districts', district);
    }
    for (const category of filters.categories ?? []) {
      params = params.append('categories', String(category));
    }
    for (const subTypeId of filters.subTypeIds ?? []) {
      params = params.append('subTypeIds', String(subTypeId));
    }

    return this.http.get<PagedResult<OpportunityRow>>(apiUrl('/api/opportunities'), { params });
  }

  locationOptions(): Observable<LocationOption[]> {
    return this.http.get<LocationOption[]>(apiUrl('/api/opportunities/locations'));
  }

  analytics(months = 9): Observable<DashboardAnalytics> {
    return this.http.get<DashboardAnalytics>(apiUrl('/api/opportunities/analytics'), {
      params: { months },
    });
  }

  // ---- interest and chat ---------------------------------------------------

  interests(box: 'all' | 'sent' | 'received' = 'all'): Observable<Interest[]> {
    return this.http.get<Interest[]>(apiUrl('/api/interests'), { params: { box } });
  }

  interest(id: string): Observable<Interest> {
    return this.http.get<Interest>(apiUrl(`/api/interests/${id}`));
  }

  sendInterest(toProfileId: string, note: string | null): Observable<Interest> {
    return this.http.post<Interest>(apiUrl('/api/interests'), { toProfileId, note });
  }

  respondToInterest(id: string, accept: boolean): Observable<Interest> {
    return this.http.post<Interest>(apiUrl(`/api/interests/${id}/respond`), { accept });
  }

  interestMessages(id: string): Observable<Message[]> {
    return this.http.get<Message[]>(apiUrl(`/api/interests/${id}/messages`));
  }

  postInterestMessage(id: string, body: string): Observable<Message> {
    return this.http.post<Message>(apiUrl(`/api/interests/${id}/messages`), { body });
  }

  // ---- deals ---------------------------------------------------------------

  /**
   * Puts terms to the other party. This does not create the deal — the deal exists
   * once they agree, because the settlement date binds them both.
   */
  proposeTerms(payload: RaiseTicketPayload): Observable<DealProposal> {
    return this.http.post<DealProposal>(apiUrl('/api/deals/proposals'), payload);
  }

  proposals(interestId: string): Observable<ProposalThread> {
    return this.http.get<ProposalThread>(apiUrl(`/api/deals/proposals/${interestId}`));
  }

  acceptProposal(id: string): Observable<DealRow> {
    return this.http.post<DealRow>(apiUrl(`/api/deals/proposals/${id}/accept`), {});
  }

  counterProposal(id: string, payload: RaiseTicketPayload): Observable<DealProposal> {
    return this.http.post<DealProposal>(apiUrl(`/api/deals/proposals/${id}/counter`), payload);
  }

  withdrawProposal(id: string): Observable<DealProposal> {
    return this.http.post<DealProposal>(apiUrl(`/api/deals/proposals/${id}/withdraw`), {});
  }

  openDeals(): Observable<DealRow[]> {
    return this.http.get<DealRow[]>(apiUrl('/api/deals/open'));
  }

  history(state: DealState | null): Observable<DealRow[]> {
    const params = state ? new HttpParams().set('state', String(state)) : undefined;
    return this.http.get<DealRow[]>(apiUrl('/api/deals/history'), { params });
  }

  deal(id: string): Observable<DealDetail> {
    return this.http.get<DealDetail>(apiUrl(`/api/deals/${id}`));
  }

  startProgress(id: string, note: string | null): Observable<DealDetail> {
    return this.http.post<DealDetail>(apiUrl(`/api/deals/${id}/progress`), { note });
  }

  confirmSettlement(id: string): Observable<DealDetail> {
    return this.http.post<DealDetail>(apiUrl(`/api/deals/${id}/settle`), {});
  }

  haltDeal(id: string, reason: string | null): Observable<DealDetail> {
    return this.http.post<DealDetail>(apiUrl(`/api/deals/${id}/halt`), { reason });
  }

  requestResume(id: string): Observable<ResumeRequest> {
    return this.http.post<ResumeRequest>(apiUrl(`/api/deals/${id}/resume-request`), {});
  }

  respondToResume(id: string, requestId: string, accept: boolean): Observable<DealDetail> {
    return this.http.post<DealDetail>(
      apiUrl(`/api/deals/${id}/resume-request/${requestId}/respond`),
      { accept },
    );
  }

  postDealMessage(id: string, body: string): Observable<Message> {
    return this.http.post<Message>(apiUrl(`/api/deals/${id}/messages`), { body });
  }

  rateDeal(id: string, stars: number, comment: string | null): Observable<Rating> {
    return this.http.post<Rating>(apiUrl(`/api/deals/${id}/rating`), { stars, comment });
  }

  // ---- notifications -------------------------------------------------------

  notifications(unreadOnly = false): Observable<AppNotification[]> {
    return this.http.get<AppNotification[]>(apiUrl('/api/notifications'), {
      params: { unreadOnly },
    });
  }

  markNotificationsRead(id?: string): Observable<void> {
    const params = id ? new HttpParams().set('id', id) : undefined;
    return this.http.post<void>(apiUrl('/api/notifications/read'), {}, { params });
  }

  // ---- admin ---------------------------------------------------------------

  adminOverview(): Observable<AdminOverview> {
    return this.http.get<AdminOverview>(apiUrl('/api/admin/overview'));
  }

  adminDirectory(filters: AdminDirectoryFilters): Observable<PagedResult<ProfileSummary>> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filters)) {
      if (value !== null && value !== undefined && value !== '') {
        params = params.set(key, String(value));
      }
    }
    return this.http.get<PagedResult<ProfileSummary>>(apiUrl('/api/admin/profiles'), { params });
  }

  adminProfile(id: string): Observable<AdminProfileDetail> {
    return this.http.get<AdminProfileDetail>(apiUrl(`/api/admin/profiles/${id}`));
  }

  verificationQueue(): Observable<VerificationQueueRow[]> {
    return this.http.get<VerificationQueueRow[]>(apiUrl('/api/admin/verification-queue'));
  }

  reviewVerification(
    id: string,
    approve: boolean,
    rejectionReason: string | null,
  ): Observable<ProfileSummary> {
    return this.http.post<ProfileSummary>(apiUrl(`/api/admin/profiles/${id}/verification`), {
      approve,
      rejectionReason,
    });
  }

  moderate(
    id: string,
    actionType: AdminActionType,
    suspensionDuration: SuspensionDuration | null,
    notes: string | null,
  ): Observable<ProfileSummary> {
    return this.http.post<ProfileSummary>(apiUrl(`/api/admin/profiles/${id}/moderate`), {
      actionType,
      suspensionDuration,
      notes,
    });
  }

  actionLog(profileId?: string): Observable<AdminActionLog[]> {
    const params = profileId ? new HttpParams().set('profileId', profileId) : undefined;
    return this.http.get<AdminActionLog[]>(apiUrl('/api/admin/action-log'), { params });
  }

  /**
   * The file itself, over the authenticated client. A plain link cannot work here:
   * the endpoint is admin-only and a browser navigation carries no bearer token.
   */
  evidence(id: string): Observable<Blob> {
    return this.http.get(apiUrl(`/api/admin/evidence/${id}`), { responseType: 'blob' });
  }
}
