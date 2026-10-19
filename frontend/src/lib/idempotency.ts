/**
 * A fresh Idempotency-Key for one "create" action. The server remembers the key, so repeating the request (double
 * click, a timeout followed by a retry, resubmitting after a failed attachment upload) returns the ticket that was
 * already created instead of creating a second one.
 *
 * crypto.randomUUID() only exists in secure contexts (https or localhost); an intranet deployment served over plain
 * http would otherwise crash, so fall back to random bytes.
 */
export function newIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID();

  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
}
