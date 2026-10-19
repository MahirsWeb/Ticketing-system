import { describe, expect, it } from 'vitest';
import { sanitizeHtml } from './sanitizeHtml';

describe('sanitizeHtml', () => {
  it('keeps ordinary formatting', () => {
    const html = '<p>Hello <strong>world</strong></p><ul><li>one</li></ul>';
    expect(sanitizeHtml(html)).toBe(html);
  });

  it.each([
    ['a script element', '<p>hi</p><script>alert(1)</script>', 'script'],
    ['an event handler', '<img src="x" onerror="alert(1)">', 'onerror'],
    ['a javascript: link', '<a href="javascript:alert(1)">x</a>', 'javascript:'],
    ['an svg payload', '<svg onload="alert(1)"><circle/></svg>', 'svg'],
    ['a form that posts elsewhere', '<form action="https://evil.example"><input name="password"></form>', 'form'],
    ['an iframe', '<iframe src="https://evil.example"></iframe>', 'iframe'],
  ])('removes %s', (_name, html, forbidden) => {
    expect(sanitizeHtml(html).toLowerCase()).not.toContain(forbidden);
  });

  it('drops a style declaration that can fetch something, and keeps the harmless ones', () => {
    const result = sanitizeHtml('<p style="color: red; background: url(https://evil.example/x.png)">x</p>');
    expect(result).toContain('color: red');
    expect(result).not.toContain('url(');
  });

  it('removes a style attribute that has nothing safe left in it', () => {
    expect(sanitizeHtml('<p style="background: url(https://evil.example/x.png)">x</p>')).toBe('<p>x</p>');
  });

  it('opens links in a new tab without handing the new page a reference back', () => {
    const result = sanitizeHtml('<a href="https://example.com">link</a>');
    expect(result).toContain('target="_blank"');
    expect(result).toContain('rel="noopener noreferrer"');
  });

  it('treats nothing as nothing', () => {
    expect(sanitizeHtml(null)).toBe('');
    expect(sanitizeHtml(undefined)).toBe('');
    expect(sanitizeHtml('')).toBe('');
  });
});
