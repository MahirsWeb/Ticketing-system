import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { useDebouncedValue } from './useDebouncedValue';
import { reconnectDelayMs } from './reconnectPolicy';
import { clearDraft, draftKey, loadDraft, saveDraft } from './drafts';
import { titleKeyFor } from './pageTitle';

describe('useDebouncedValue', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('passes on a value only after it has stopped changing', () => {
    const { result, rerender } = renderHook(({ value }) => useDebouncedValue(value, 300), { initialProps: { value: 'I' } });

    rerender({ value: 'Iz' });
    act(() => vi.advanceTimersByTime(200));
    rerender({ value: 'Izv' });
    act(() => vi.advanceTimersByTime(200));
    expect(result.current).toBe('I'); // 400 ms in, but the last change was only 200 ms ago

    act(() => vi.advanceTimersByTime(150));
    expect(result.current).toBe('Izv'); // typing "Iz" in between never reached the outside
  });
});

describe('reconnectDelayMs', () => {
  it('starts at once and grows to half a minute and stays there for ever', () => {
    const exact = () => 0.5; // no jitter: (0.8 + 0.5 * 0.4) = 1.0
    expect([0, 1, 2, 3, 4, 5, 6, 50, 5000].map((n) => reconnectDelayMs(n, exact))).toEqual([0, 2000, 5000, 10000, 20000, 30000, 30000, 30000, 30000]);
  });

  it('never gives up: there is always a next delay, and it is never negative', () => {
    for (let n = 0; n < 200; n++) {
      const delay = reconnectDelayMs(n);
      expect(Number.isFinite(delay)).toBe(true);
      expect(delay).toBeGreaterThanOrEqual(0);
    }
  });

  it('spreads the pages that lost the server together over a few seconds', () => {
    expect(reconnectDelayMs(3, () => 0)).toBe(8000);
    expect(reconnectDelayMs(3, () => 0.999)).toBeGreaterThan(11000);
    expect(reconnectDelayMs(0, () => 0.999)).toBe(0);
  });

  it('copes with a nonsense count', () => {
    expect(reconnectDelayMs(-4, () => 0.5)).toBe(0);
  });
});

describe('drafts', () => {
  it('keeps unsent text for the tab and gives it back', () => {
    const key = draftKey('ticket-1', 'Response');
    saveDraft(key, '<p>half a reply</p>');

    expect(loadDraft(key)).toBe('<p>half a reply</p>');
    expect(loadDraft(draftKey('ticket-1', 'InternalNote'))).toBe(''); // a note is another draft
    expect(loadDraft(draftKey('ticket-2', 'Response'))).toBe('');     // and so is another ticket
  });

  it('forgets a draft that has been sent or emptied', () => {
    const key = draftKey('ticket-1', 'Response');
    saveDraft(key, '<p>text</p>');
    clearDraft(key);
    expect(loadDraft(key)).toBe('');

    saveDraft(key, '<p>text</p>');
    saveDraft(key, '<p></p>');
    expect(loadDraft(key)).toBe('');
  });

  it('works without storage rather than failing the page', () => {
    const broken = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const failing = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    expect(loadDraft('x')).toBe('');
    expect(() => saveDraft('x', '<p>text</p>')).not.toThrow();

    broken.mockRestore();
    failing.mockRestore();
  });
});

describe('titleKeyFor', () => {
  it.each([
    ['/', 'nav_dashboard'],
    ['/login', 'login_signIn'],
    ['/tickets', 'nav_tickets'],
    ['/tickets/', 'nav_tickets'],
    ['/tickets/new', 'nav_submitTicket'],
    ['/tickets/new-on-behalf', 'title_newOnBehalf'],
    ['/tickets/0b7e2f1c-1111-2222-3333-444455556666', 'title_ticket'],
    ['/tasks', 'nav_task'],
    ['/users', 'nav_users'],
    ['/users/abc', 'title_user'],
    ['/companies/abc', 'title_company'],
    ['/ai-assistant', 'nav_aiAssistant'],
    ['/knowledge-base/similar', 'title_similar'],
    ['/settings', 'nav_settings'],
    ['/emails', 'nav_emails'],
    ['/emails/callback', 'nav_emails'],
    ['/forgot-password', 'title_account'],
    ['/set-new-password', 'title_account'],
    ['/no/such/page', 'title_notFound']
  ])('%s is named %s', (path, key) => {
    expect(titleKeyFor(path)).toBe(key);
  });
});
