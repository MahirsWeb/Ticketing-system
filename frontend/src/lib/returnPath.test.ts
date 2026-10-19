import { describe, expect, it } from 'vitest';
import { safeReturnPath } from './returnPath';

describe('safeReturnPath', () => {
  it.each(['/tickets', '/tickets/abc?status=New&page=2', '/'])('returns the in-app path %s unchanged', (path) => {
    expect(safeReturnPath(path)).toBe(path);
  });

  it.each([
    ['an absolute address', 'https://evil.example/phish'],
    ['a protocol-relative address', '//evil.example/phish'],
    ['the sign-in page itself', '/login'],
    ['a relative path without a slash', 'tickets'],
    ['a javascript: address', 'javascript:alert(1)'],
    ['nothing', undefined],
    ['something that is not text', { path: '/tickets' }],
  ])('sends %s to the start page', (_name, value) => {
    expect(safeReturnPath(value)).toBe('/');
  });
});
