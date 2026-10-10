import { Injectable, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { SaakhApi } from '../api/saakh.api';
import { EvidenceDocument } from '../models/domain';
// Type-only: the panel itself is loaded on first use, so it stays out of the
// initial bundle that every signed-out visitor downloads.
import type { EvidencePreviewData } from '../../shared/evidence-preview.dialog';
import { ToastService } from './toast.service';

/**
 * Opens a submitted evidence document for review.
 *
 * It cannot be a plain link: the endpoint is admin-only and the access token is
 * attached by the HTTP interceptor, so a browser navigating to the URL sends no
 * Authorization header and gets a 401 page.
 *
 * It is not a new tab either. The file has to be fetched before there is anything
 * to show, and a popup opened after that round trip is treated as unsolicited —
 * `window.open(url, '_blank', 'noopener')` also returns null outright, which is how
 * the first attempt ended up silently downloading into a blank tab. The document
 * opens in a panel over the page instead, with downloading offered rather than
 * forced.
 */
// Provided by the admin shell rather than in root: this is an administrator's tool,
// and a root provider pulls it and MatDialog into the bundle every visitor downloads
// before they have even signed in.
@Injectable()
export class EvidenceViewer {
  private readonly api = inject(SaakhApi);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);

  open(document: EvidenceDocument): void {
    this.api.evidence(document.id).subscribe({
      next: async (blob) => {
        // The server's content type is authoritative; a blob from HttpClient can
        // arrive as application/octet-stream and would then render as nothing.
        const typed = blob.type ? blob : new Blob([blob], { type: document.contentType });
        const objectUrl = URL.createObjectURL(typed);

        const data: EvidencePreviewData = { document, objectUrl };

        const { EvidencePreviewDialog } = await import('../../shared/evidence-preview.dialog');

        this.dialog
          .open(EvidencePreviewDialog, {
            data,
            width: '880px',
            maxWidth: '94vw',
            autoFocus: false,
          })
          .afterClosed()
          // Released only once the panel is gone: revoking while it is open blanks
          // the image that is being looked at.
          .subscribe(() => URL.revokeObjectURL(objectUrl));
      },
      error: (error: unknown) => this.toast.error(error, 'That document could not be opened.'),
    });
  }
}
