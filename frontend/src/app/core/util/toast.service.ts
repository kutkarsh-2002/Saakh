import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ApiError } from '../models/domain';

/**
 * One place that turns an API failure into words a vendor can act on. The API
 * already returns plain-language messages next to each business rule, so this
 * surfaces them verbatim rather than inventing its own wording.
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly snackBar = inject(MatSnackBar);

  success(message: string): void {
    this.snackBar.open(message, 'Dismiss', { duration: 4000 });
  }

  error(error: unknown, fallback = 'Something went wrong. Try again.'): void {
    this.snackBar.open(messageFor(error, fallback), 'Dismiss', {
      duration: 7000,
      panelClass: 'sk-snack-error',
    });
  }

  info(message: string): void {
    this.snackBar.open(message, 'Dismiss', { duration: 5000 });
  }
}

export function messageFor(error: unknown, fallback = 'Something went wrong. Try again.'): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) {
      return 'We could not reach Saakh. Check your connection and try again.';
    }

    if (error.status === 429) {
      return 'Too many attempts. Wait a minute and try again.';
    }

    const body = error.error as ApiError | { errors?: Record<string, string[]> } | string | null;

    if (typeof body === 'string' && body.trim()) {
      return body;
    }

    if (body && typeof body === 'object') {
      if ('message' in body && typeof body.message === 'string' && body.message) {
        return body.message;
      }

      // FluentValidation / ProblemDetails validation shape.
      if ('errors' in body && body.errors) {
        const first = Object.values(body.errors).flat()[0];
        if (typeof first === 'string') {
          return first;
        }
      }
    }
  }

  return fallback;
}
