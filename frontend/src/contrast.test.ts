import { describe, expect, it } from 'vitest';

/**
 * The page is `bg-slate-200` (layouts/AppLayout.tsx) and its cards are white. Small text must reach a contrast of 4.5:1 on both
 * (WCAG AA). On the gray page, `text-slate-500` has 3.86:1 and `text-amber-600` on white 3.06:1, so the lightest text colours
 * allowed are slate-600 and amber-700. A class that is a decoration (marked `aria-hidden`) or sits on the dark navigation bar
 * is the exception, and the few of those that are not marked are listed here by file, with how many of them there are.
 */
const sources = import.meta.glob(['./**/*.tsx', '!./**/*.test.tsx'], { query: '?raw', import: 'default', eager: true }) as Record<string, string>;

const TOO_LIGHT = /(?<![\w-])text-(?:slate|gray|zinc|neutral|stone)-(?:100|200|300|400|500)\b|(?<![\w-])text-(?:amber|yellow|orange|lime|green|emerald|teal|cyan|sky)-(?:100|200|300|400|500|600)\b/g;

/** On the dark navigation bar or a decoration without a text alternative: [file ending, how many are allowed]. */
const ALLOWED: Record<string, number> = {
  'components/LanguageSwitcher.tsx': 1, // the inactive language on the dark bar
  'layouts/AppLayout.tsx': 4, // the menu, the language, the name and the sign-out button on the dark bar
  'components/dateRange/DaysView.tsx': 1, // a day in the future: the button is disabled, and a disabled control is exempt from the contrast rule
};

describe('text colours', () => {
  it('has sources to look at', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(100);
  });

  it('uses no text colour lighter than slate-600 (amber-700) outside the listed decorations', () => {
    const offenders: string[] = [];
    for (const [path, text] of Object.entries(sources)) {
      const name = path.replace('./', '');
      let allowed = ALLOWED[name] ?? 0;
      text.split('\n').forEach((line, i) => {
        const hits = line.match(TOO_LIGHT) ?? [];
        for (const hit of hits) {
          if (line.includes('aria-hidden')) continue;
          if (allowed > 0) {
            allowed--;
            continue;
          }
          offenders.push(`${name}:${i + 1} ${hit}`);
        }
      });
    }
    expect(offenders).toEqual([]);
  });

  // green-700 on green-100 measured 4.49:1 in the browser, a hair under the line, so a pill of that tint has the darker text.
  it('puts no green-700 text on a green-100 pill', () => {
    const offenders = Object.entries(sources).flatMap(([path, text]) =>
      text.split('\n').flatMap((line, i) => (/bg-green-100[^'"`]*text-green-700|text-green-700[^'"`]*bg-green-100/.test(line) ? [`${path}:${i + 1}`] : []))
    );
    expect(offenders).toEqual([]);
  });

  it('lists only decorations that still exist (an entry nobody uses any more is removed)', () => {
    for (const [name, count] of Object.entries(ALLOWED)) {
      const text = sources['./' + name];
      expect(text, `${name} is gone`).toBeDefined();
      const hits = text.split('\n').filter((l) => !l.includes('aria-hidden')).reduce((n, l) => n + (l.match(TOO_LIGHT)?.length ?? 0), 0);
      expect(hits, `${name} has ${hits} light classes, ${count} allowed`).toBe(count);
    }
  });
});
