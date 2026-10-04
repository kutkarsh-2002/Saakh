import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { AppNotification, DealState, InterestStatus, Message, VerificationStatus } from '../models/domain';
import { SessionStore } from '../auth/session.store';
import { HUB_URL } from '../api/api.config';

export interface DealStateChanged {
  dealId: string;
  state: DealState;
}

export interface InterestChanged {
  interestId: string;
  status: InterestStatus;
}

export interface VerificationChanged {
  status: VerificationStatus;
  rejectionReason?: string | null;
  profileId?: string;
}

/**
 * The live layer: verification status pushes, Interest notifications and deal
 * chat. Reconnects automatically, because this user base is on patchy mobile
 * data and a dropped socket must not leave the chat silently dead.
 */
@Injectable({ providedIn: 'root' })
export class RealtimeService {
  private readonly store = inject(SessionStore);
  private readonly destroyRef = inject(DestroyRef);

  private connection: HubConnection | null = null;
  private joinedDeals = new Set<string>();

  /** In-flight start, so concurrent callers share one connection attempt. */
  private starting: Promise<void> | null = null;

  readonly connected = signal(false);

  readonly notifications$ = new Subject<AppNotification>();
  readonly messages$ = new Subject<Message>();
  readonly dealState$ = new Subject<DealStateChanged>();
  readonly interest$ = new Subject<InterestChanged>();
  readonly verification$ = new Subject<VerificationChanged>();

  constructor() {
    this.destroyRef.onDestroy(() => void this.stop());
  }

  async start(): Promise<void> {
    if (this.starting) {
      return this.starting;
    }

    if (this.connection || !this.store.accessToken()) {
      return;
    }

    this.starting = this.connect();
    try {
      await this.starting;
    } finally {
      this.starting = null;
    }
  }

  private async connect(): Promise<void> {

    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL, {
        // The WebSocket handshake cannot set an Authorization header, so the
        // token travels as a query parameter; the API reads it for /hubs only.
        accessTokenFactory: () => this.store.accessToken() ?? '',
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 20000])
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('notification', (payload: AppNotification) => this.notifications$.next(payload));
    connection.on('messageReceived', (payload: Message) => this.messages$.next(payload));
    connection.on('dealStateChanged', (payload: DealStateChanged) => this.dealState$.next(payload));
    connection.on('interestChanged', (payload: InterestChanged) => this.interest$.next(payload));
    connection.on('verificationChanged', (payload: VerificationChanged) =>
      this.verification$.next(payload),
    );
    connection.on('profileModerated', (payload: VerificationChanged) =>
      this.verification$.next(payload),
    );

    connection.onreconnected(() => {
      this.connected.set(true);
      // Group membership does not survive a reconnect, so rejoin every open
      // deal workspace.
      void this.flushDealGroups();
    });

    connection.onreconnecting(() => this.connected.set(false));
    connection.onclose(() => this.connected.set(false));

    this.connection = connection;

    try {
      await connection.start();
      this.connected.set(true);

      // A workspace opened before the socket finished connecting has already
      // queued its deal id; joining here is what makes that join actually take
      // effect, rather than silently dropping it.
      await this.flushDealGroups();
    } catch {
      // A failed socket must not break the app: every screen also loads its
      // data over REST, so the UI degrades to refresh-on-navigate.
      this.connected.set(false);
    }
  }

  private async flushDealGroups(): Promise<void> {
    const connection = this.connection;
    if (connection?.state !== HubConnectionState.Connected) {
      return;
    }

    for (const dealId of this.joinedDeals) {
      await connection.invoke('JoinDeal', dealId).catch(() => undefined);
    }
  }

  async stop(): Promise<void> {
    this.joinedDeals.clear();
    const connection = this.connection;
    this.connection = null;
    this.connected.set(false);
    if (connection) {
      await connection.stop().catch(() => undefined);
    }
  }

  async joinDeal(dealId: string): Promise<void> {
    this.joinedDeals.add(dealId);

    // Waits for the connection rather than racing it: a workspace routed to
    // immediately after sign-in would otherwise never join its deal group.
    await this.start();

    if (this.connection?.state === HubConnectionState.Connected) {
      await this.connection.invoke('JoinDeal', dealId).catch(() => undefined);
    }
  }

  async leaveDeal(dealId: string): Promise<void> {
    this.joinedDeals.delete(dealId);
    if (this.connection?.state === HubConnectionState.Connected) {
      await this.connection.invoke('LeaveDeal', dealId).catch(() => undefined);
    }
  }
}
