/**
 * True when editor HTML shows nothing: no text and no picture. `<p></p>` is the obvious case, but `<p> </p>` and `<p>&nbsp;</p>` look
 * exactly as empty to the person who reads them and used to pass a check of the HTML string. A picture alone (a pasted screenshot) is
 * content. The server makes the same decision, so a request that skips this check is refused there too.
 */
export function isBlankHtml(html: string | null | undefined): boolean {
  if (!html || !html.trim()) return true;
  if (/<img\b/i.test(html)) return false;
  const text = new DOMParser().parseFromString(html, 'text/html').body.textContent ?? '';
  // \s covers the no-break space; the zero-width characters are invisible too.
  return text.replace(/[\s\u200B-\u200D\uFEFF]/g, '') === '';
}
