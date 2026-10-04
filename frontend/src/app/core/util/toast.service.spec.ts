import { HttpErrorResponse } from '@angular/common/http';
import { messageFor } from './toast.service';

/**
 * The API states each business rule in plain language next to the rule itself.
 * This is the one place that decides a vendor reads those words rather than a
 * status code — so the fallback is the failure case, not the normal path.
 */
describe('messageFor', () => {
  const response = (status: number, body: unknown) =>
    new HttpErrorResponse({ status, statusText: 'Error', error: body, url: '/api/deals' });

  it('surfaces the API message verbatim', () => {
    const message = messageFor(
      response(409, { message: 'This deal is frozen while the account is suspended.' }),
    );

    expect(message).toBe('This deal is frozen while the account is suspended.');
  });

  it('names the connection rather than blaming the user when the API is unreachable', () => {
    expect(messageFor(response(0, null))).toContain('could not reach Saakh');
  });

  it('explains a rate limit as something to wait out', () => {
    expect(messageFor(response(429, null))).toContain('Wait a minute');
  });

  it('shows the first field error from a validation failure', () => {
    const message = messageFor(
      response(400, { errors: { Gstin: ['A GSTIN is 15 characters.'], Phone: ['Required.'] } }),
    );

    expect(message).toBe('A GSTIN is 15 characters.');
  });

  it('accepts a bare string body', () => {
    expect(messageFor(response(400, 'Pick a district before continuing.'))).toBe(
      'Pick a district before continuing.',
    );
  });

  it('falls back for a shape it does not recognise', () => {
    expect(messageFor(response(500, { trace: 'x' }), 'Could not save that.')).toBe(
      'Could not save that.',
    );
  });

  it('falls back for something that is not an HTTP error at all', () => {
    expect(messageFor(new TypeError('undefined is not a function'))).toContain('Something went wrong');
  });
});
