import { useEffect, useRef } from 'react';

// Self-contained placeholder (no network dependency) shown in place of any <img> that fails to
// load — mainly osTicket-imported tickets whose inline email images (cid: references) were never
// migrated, so the browser's raw broken-image icon would otherwise show instead.
const BROKEN_IMAGE_PLACEHOLDER =
  'data:image/svg+xml;utf8,' +
  encodeURIComponent(
    `<svg xmlns="http://www.w3.org/2000/svg" width="220" height="130" viewBox="0 0 220 130">
      <rect width="220" height="130" fill="#f1f5f9"/>
      <rect x="0.5" y="0.5" width="219" height="129" fill="none" stroke="#cbd5e1"/>
      <g transform="translate(78,32)" fill="none" stroke="#94a3b8" stroke-width="2">
        <rect x="0" y="0" width="64" height="48" rx="3"/>
        <circle cx="15" cy="15" r="5"/>
        <path d="M0 41 L19 24 L32 34 L47 17 L64 36" />
      </g>
      <text x="110" y="104" text-anchor="middle" font-family="Arial, sans-serif" font-size="11" fill="#94a3b8">Image unavailable</text>
    </svg>`
  );

/**
 * Renders untrusted HTML (ticket/message bodies) while swapping any image that fails to load for
 * a neutral placeholder instead of the browser's default broken-image glyph.
 */
export function SafeHtml({ html, className }: { html: string; className?: string }) {
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const container = ref.current;
    if (!container) return;

    const cleanups: Array<() => void> = [];
    container.querySelectorAll('img').forEach((img) => {
      const handleError = () => {
        img.src = BROKEN_IMAGE_PLACEHOLDER;
        img.style.maxWidth = '220px';
        img.style.maxHeight = '130px';
      };
      img.addEventListener('error', handleError);
      cleanups.push(() => img.removeEventListener('error', handleError));
    });

    return () => cleanups.forEach((fn) => fn());
  }, [html]);

  return <div ref={ref} className={className} dangerouslySetInnerHTML={{ __html: html }} />;
}
