import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { EvidenceDocument } from '../core/models/domain';
import { formatDateTime } from '../core/util/format';

export interface EvidencePreviewData {
  document: EvidenceDocument;
  /** Object URL for the fetched file. The caller owns it and revokes it on close. */
  objectUrl: string;
}

/**
 * Shows a submitted proof document without leaving the page.
 *
 * A new tab was the obvious first answer and the wrong one: the file has to be
 * fetched with a bearer token, so the tab can only be pointed at a blob URL after
 * the download resolves — by which time the browser treats the popup as unsolicited.
 * Reviewing evidence is also the one thing an administrator does repeatedly, and
 * bouncing between tabs to do it is worse than a panel that opens over the queue.
 *
 * Downloading stays available, as a choice rather than as the only outcome.
 */
@Component({
  selector: 'sk-evidence-preview-dialog',
  imports: [MatDialogModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title class="title">
      <span>{{ data.document.documentType }}</span>
      <span class="title__file">{{ data.document.fileName }}</span>
    </h2>

    <div mat-dialog-content class="content">
      @if (isImage()) {
        <img
          class="preview"
          [src]="data.objectUrl"
          [alt]="data.document.documentType + ' submitted as ' + data.document.fileName"
          (error)="failed.set(true)"
        />
      } @else if (isPdf()) {
        <iframe
          class="preview preview--pdf"
          [src]="safeUrl()"
          [title]="data.document.documentType"
        ></iframe>
      } @else {
        <p class="unsupported">
          <span class="material-symbols-rounded" aria-hidden="true">draft</span>
          This is a {{ data.document.contentType }} file, which cannot be shown here.
          Download it to open it.
        </p>
      }

      @if (failed()) {
        <p class="unsupported">
          <span class="material-symbols-rounded" aria-hidden="true">broken_image</span>
          The file could not be displayed. It may be corrupt — download it to check.
        </p>
      }

      <p class="meta">
        Submitted {{ submittedAt }} · {{ sizeLabel() }}
      </p>
    </div>

    <div mat-dialog-actions class="actions">
      <a
        class="sk-btn sk-btn--secondary"
        [href]="data.objectUrl"
        [download]="data.document.fileName"
      >
        <span class="material-symbols-rounded" aria-hidden="true">download</span>
        Download
      </a>
      <button type="button" class="sk-btn sk-btn--primary" (click)="close()">Close</button>
    </div>
  `,
  styles: [
    `
      .title {
        display: grid;
        gap: 2px;
      }

      .title__file {
        font-family: var(--sk-font-ui);
        font-size: 0.8125rem;
        font-weight: 400;
        color: var(--sk-ink-muted);
      }

      .content {
        display: grid;
        gap: var(--sk-space-3);
      }

      /* The document fills the panel but is never blown up past its own size: a
         licence photographed on a phone is unreadable when it is upscaled. */
      .preview {
        display: block;
        max-width: 100%;
        max-height: 68vh;
        margin: 0 auto;
        border-radius: var(--sk-radius);
        background: var(--sk-surface-sunken);
        object-fit: contain;
      }

      .preview--pdf {
        width: 100%;
        height: 68vh;
        border: 1px solid var(--sk-line-strong);
      }

      .unsupported {
        display: flex;
        align-items: center;
        gap: var(--sk-space-2);
        margin: 0;
        padding: var(--sk-space-4);
        background: var(--sk-surface-sunken);
        border-radius: var(--sk-radius);
        color: var(--sk-ink-muted);
        font-size: 0.875rem;
      }

      .meta {
        margin: 0;
        font-size: 0.8125rem;
        color: var(--sk-ink-muted);
      }

      .actions {
        display: flex;
        justify-content: flex-end;
        gap: var(--sk-space-2);
      }
    `,
  ],
})
export class EvidencePreviewDialog {
  readonly data = inject<EvidencePreviewData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject<MatDialogRef<EvidencePreviewDialog>>(MatDialogRef);
  private readonly sanitizer = inject(DomSanitizer);

  readonly failed = signal(false);

  readonly submittedAt = formatDateTime(this.data.document.submittedAt);

  readonly isImage = computed(() => this.data.document.contentType.startsWith('image/'));
  readonly isPdf = computed(() => this.data.document.contentType === 'application/pdf');

  /** A blob: URL this app created moments ago, so it is safe to trust as a frame source. */
  readonly safeUrl = computed<SafeResourceUrl>(() =>
    this.sanitizer.bypassSecurityTrustResourceUrl(this.data.objectUrl),
  );

  readonly sizeLabel = computed(() => {
    const bytes = this.data.document.sizeBytes;
    return bytes < 1024 * 1024
      ? `${Math.max(1, Math.round(bytes / 1024))} KB`
      : `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  });

  close(): void {
    this.dialogRef.close();
  }
}
