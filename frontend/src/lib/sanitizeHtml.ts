import DOMPurify from 'dompurify';

// Inline styles are kept (rich-text alignment, colours and pasted email formatting depend on them), but a
// declaration that can load or run anything is dropped: url() makes the browser fetch an attacker's address
// (tracking, data exfiltration), the rest are legacy script hooks.
const UNSAFE_STYLE_VALUE = /url\s*\(|expression\s*\(|javascript:|@import|behavior\s*:|-moz-binding/i;

function safeStyle(style: string): string {
  return style
    .split(';')
    .map((declaration) => declaration.trim())
    .filter((declaration) => declaration.length > 0 && !UNSAFE_STYLE_VALUE.test(declaration))
    .join('; ');
}

DOMPurify.addHook('uponSanitizeAttribute', (_node, data) => {
  if (data.attrName !== 'style') return;
  const cleaned = safeStyle(data.attrValue);
  if (cleaned) {
    data.attrValue = cleaned;
  } else {
    data.keepAttr = false;
  }
});

// Links inside ticket/message bodies come from outside (clients, inbound emails): open them in a new tab and
// never hand the opened page a reference back to this one.
DOMPurify.addHook('afterSanitizeAttributes', (node) => {
  if (node.tagName === 'A' && node.hasAttribute('href')) {
    node.setAttribute('target', '_blank');
    node.setAttribute('rel', 'noopener noreferrer');
  }
});

/**
 * Second line of defence for HTML that is rendered with dangerouslySetInnerHTML. The API already sanitises
 * ticket and message bodies on write and on read, but the browser must not depend on that alone: anything that
 * reaches the page (an old row, a future endpoint that forgets to sanitise) is cleaned here too.
 *
 * The HTML profile excludes SVG and MathML, which are the usual route for script injection through markup that
 * otherwise looks like plain formatting. Forms and controls are removed as well: a ticket is text, and a fake
 * login form posting to another site is a ready-made phishing page inside a trusted application.
 */
export function sanitizeHtml(html: string | null | undefined): string {
  if (!html) return '';
  return DOMPurify.sanitize(html, {
    USE_PROFILES: { html: true },
    FORBID_TAGS: ['form', 'input', 'button', 'textarea', 'select', 'option', 'style', 'link', 'meta', 'base'],
    FORBID_ATTR: ['action', 'formaction'],
  });
}
