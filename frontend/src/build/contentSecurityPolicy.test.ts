import { describe, expect, it } from 'vitest';
import { apiOrigin, contentSecurityPolicy } from './contentSecurityPolicy';

const directive = (policy: string, name: string) => policy.split('; ').find((d) => d.startsWith(name + ' '))?.slice(name.length + 1).split(' ');

describe('the content security policy of the built application', () => {
  describe('the address of the API', () => {
    it.each([
      ['http://localhost:5114', 'http://localhost:5114'],
      ['https://api.firma.ba', 'https://api.firma.ba'],
      ['https://api.firma.ba/', 'https://api.firma.ba'],
      ['https://api.firma.ba/some/path?x=1#y', 'https://api.firma.ba'],
      ['https://api.firma.ba:8443', 'https://api.firma.ba:8443'],
    ])('is taken as the origin of %s', (given, origin) => {
      expect(apiOrigin(given)).toBe(origin);
    });

    it.each([undefined, '', '/', 'api', '/api', 'ftp://api.firma.ba', 'javascript:alert(1)', 'data:text/html,x', "https://x.test; script-src *", "https://x.test'"])(
      'is not taken from %j',
      (given) => {
        expect(apiOrigin(given)).toBeNull();
      }
    );
  });

  describe('what it allows', () => {
    it('lets the page talk to itself, to the API and to its live connection, and to nobody else', () => {
      const policy = contentSecurityPolicy('https://api.firma.ba');

      expect(directive(policy, 'connect-src')).toEqual(["'self'", 'https://api.firma.ba', 'wss://api.firma.ba']);
    });

    it('uses a plain WebSocket for an API that is not on https', () => {
      expect(directive(contentSecurityPolicy('http://localhost:5114'), 'connect-src')).toEqual(["'self'", 'http://localhost:5114', 'ws://localhost:5114']);
    });

    it('lets pictures come from the page, from the API, from inside the page (data and blob), and from nowhere else', () => {
      expect(directive(contentSecurityPolicy('https://api.firma.ba'), 'img-src')).toEqual(["'self'", 'data:', 'blob:', 'https://api.firma.ba']);
    });

    it('lets the page talk and load pictures only from itself when there is no address of an API', () => {
      for (const given of [undefined, '', '/api']) {
        const policy = contentSecurityPolicy(given);
        expect(directive(policy, 'connect-src')).toEqual(["'self'"]);
        expect(directive(policy, 'img-src')).toEqual(["'self'", 'data:', 'blob:']);
      }
    });

    it('does not let a mistyped or hostile address add anything to the policy', () => {
      const policy = contentSecurityPolicy("https://x.test; script-src *; connect-src *");

      expect(policy).toBe(contentSecurityPolicy(undefined));
      expect(policy).not.toContain('*');
    });
  });

  describe('what it never allows', () => {
    const policy = contentSecurityPolicy('https://api.firma.ba');

    it('runs scripts only from the application itself, and never evaluates text as code', () => {
      expect(directive(policy, 'script-src')).toEqual(["'self'"]);
      expect(policy).not.toContain('unsafe-eval');
      expect(directive(policy, 'style-src')).toEqual(["'self'", "'unsafe-inline'"]);
    });

    it.each([
      ['object-src', ["'none'"]],
      ['base-uri', ["'self'"]],
      ['form-action', ["'self'"]],
      ['default-src', ["'self'"]],
      ['font-src', ["'self'", 'data:']],
    ])('holds %s to %j', (name, expected) => {
      expect(directive(policy, name)).toEqual(expected);
    });

    it('allows nothing by a wildcard', () => {
      expect(policy).not.toMatch(/\s\*(\s|;|$)/);
    });
  });
});
