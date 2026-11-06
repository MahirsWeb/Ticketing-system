import { describe, expect, it } from 'vitest';
import { contentSecurityPolicy } from './contentSecurityPolicy';
import { contentSecurityPolicyPlugin } from './contentSecurityPolicyPlugin';

type Hooks = {
  apply?: string;
  configResolved: (config: { mode: string; envDir: string }) => void;
  transformIndexHtml: () => { tag: string; attrs: Record<string, string>; injectTo: string }[];
};

const plugin = (api: string | undefined) => contentSecurityPolicyPlugin(() => api) as unknown as Hooks;

describe('the plugin that puts the policy into the built page', () => {
  it('works on a build, and not on the development server, which needs what the policy forbids', () => {
    expect(plugin('https://api.firma.ba').apply).toBe('build');
  });

  it('adds one meta tag, first in the head, with the policy for the API that was given', () => {
    const p = plugin('https://api.firma.ba');
    p.configResolved({ mode: 'production', envDir: '.' });

    const tags = p.transformIndexHtml();

    expect(tags).toEqual([
      { tag: 'meta', attrs: { 'http-equiv': 'Content-Security-Policy', content: contentSecurityPolicy('https://api.firma.ba') }, injectTo: 'head-prepend' },
    ]);
    expect(tags[0].attrs.content).toContain('connect-src \'self\' https://api.firma.ba wss://api.firma.ba');
  });

  it('allows only the page itself when it was built without the address of an API', () => {
    const p = plugin(undefined);
    p.configResolved({ mode: 'production', envDir: '.' });

    expect(p.transformIndexHtml()[0].attrs.content).toBe(contentSecurityPolicy(undefined));
  });

  it('asks for the address with the mode and the folder the build is using', () => {
    const asked: { mode: string; envDir: string }[] = [];
    const p = contentSecurityPolicyPlugin((config) => {
      asked.push(config);
      return 'https://api.firma.ba';
    }) as unknown as Hooks;

    p.configResolved({ mode: 'staging', envDir: '/app/frontend' });

    expect(asked).toEqual([{ mode: 'staging', envDir: '/app/frontend' }]);
  });
});
