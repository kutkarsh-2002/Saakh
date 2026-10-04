import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { ResumeRequestStatus } from '../../core/models/domain';
import { SaakhApi } from '../../core/api/saakh.api';
import { ToastService } from '../../core/util/toast.service';
import { ConfirmDialogData } from '../../shared/confirm-dialog';
import { deal } from '../../testing/fixtures';
import { DealActions } from './deal-actions';

/**
 * Halting is the only action in the product that permanently marks a profile at
 * fault, and resuming needs both sides. Both consequences have to be on screen
 * before the click, and dismissing a dialog has to change nothing at all — a
 * stray call here would record a fault the vendor never agreed to.
 */
describe('DealActions', () => {
  let actions: DealActions;
  let api: jasmine.SpyObj<SaakhApi>;
  let dialogResult: unknown;
  let openedWith!: ConfirmDialogData;
  let onDone: jasmine.Spy;

  beforeEach(() => {
    api = jasmine.createSpyObj<SaakhApi>('SaakhApi', [
      'rateDeal',
      'haltDeal',
      'requestResume',
      'respondToResume',
    ]);
    api.rateDeal.and.returnValue(of({}) as never);
    api.haltDeal.and.returnValue(of({}) as never);
    api.requestResume.and.returnValue(of({}) as never);
    api.respondToResume.and.returnValue(of({}) as never);

    dialogResult = undefined;
    onDone = jasmine.createSpy('onDone');

    const dialog = {
      open: (_component: unknown, config: { data: ConfirmDialogData }) => {
        openedWith = config.data;
        return { afterClosed: () => of(dialogResult) };
      },
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: SaakhApi, useValue: api },
        { provide: MatDialog, useValue: dialog },
        { provide: ToastService, useValue: jasmine.createSpyObj('ToastService', ['success', 'error', 'info']) },
      ],
    });

    actions = TestBed.inject(DealActions);
  });

  describe('halting', () => {
    it('states the fault consequence and names the deal before anything happens', () => {
      actions.halt(deal(), onDone);

      expect(openedWith.title).toContain('SK-2026-0042');
      expect(openedWith.consequence).toContain('at fault');
      expect(openedWith.destructive).toBeTrue();
    });

    it('does nothing when the dialog is dismissed', () => {
      dialogResult = { confirmed: false };

      actions.halt(deal(), onDone);

      expect(api.haltDeal).not.toHaveBeenCalled();
      expect(onDone).not.toHaveBeenCalled();
    });

    it('passes the reason through when confirmed', () => {
      dialogResult = { confirmed: true, reason: 'Consignment never arrived.' };

      actions.halt(deal(), onDone);

      expect(api.haltDeal).toHaveBeenCalledWith(
        'dd000000-0000-0000-0000-000000000001',
        'Consignment never arrived.',
      );
      expect(onDone).toHaveBeenCalled();
    });
  });

  describe('resuming', () => {
    it('says plainly that a one-sided request changes nothing', () => {
      actions.requestResume(deal(), onDone);

      expect(openedWith.consequence).toContain('only returns to Progress once they accept');
    });

    it('sends the request when confirmed', () => {
      dialogResult = { confirmed: true };

      actions.requestResume(deal(), onDone);

      expect(api.requestResume).toHaveBeenCalledWith('dd000000-0000-0000-0000-000000000001');
      expect(onDone).toHaveBeenCalled();
    });

    it('cannot accept a resume on a deal that has no pending request', () => {
      dialogResult = { confirmed: true };

      actions.acceptResume(deal({ pendingResumeRequest: null }), onDone);

      expect(api.respondToResume).not.toHaveBeenCalled();
    });

    it('accepts the specific pending request, not just the deal', () => {
      dialogResult = { confirmed: true };
      const row = deal({
        pendingResumeRequest: {
          id: 'ee000000-0000-0000-0000-000000000001',
          dealId: 'dd000000-0000-0000-0000-000000000001',
          requestedByProfileId: 'aa000000-0000-0000-0000-000000000002',
          requestedByMe: false,
          status: ResumeRequestStatus.Pending,
          createdAt: '2026-09-20T10:00:00+05:30',
        },
      });

      actions.acceptResume(row, onDone);

      expect(api.respondToResume).toHaveBeenCalledWith(
        'dd000000-0000-0000-0000-000000000001',
        'ee000000-0000-0000-0000-000000000001',
        true,
      );
    });
  });

  describe('rating', () => {
    it('does nothing when the rating dialog is dismissed', () => {
      actions.rate(deal(), onDone);

      expect(api.rateDeal).not.toHaveBeenCalled();
    });

    it('submits the stars and comment the vendor chose', () => {
      dialogResult = { stars: 4, comment: 'Settled a week late but paid in full.' };

      actions.rate(deal(), onDone);

      expect(api.rateDeal).toHaveBeenCalledWith(
        'dd000000-0000-0000-0000-000000000001',
        4,
        'Settled a week late but paid in full.',
      );
      expect(onDone).toHaveBeenCalled();
    });
  });
});
