import { loadEnv, type Plugin } from 'vite';
import { contentSecurityPolicy } from './contentSecurityPolicy.ts';

/**
 * Puts the Content-Security-Policy into the page that is built, with the address of the API it was built for (SEC-03): the page then holds
 * whoever serves the files. Not in development, where the dev server needs what the policy forbids.
 * `readApiBaseUrl` is what finds the address; it is a parameter so that a test can give one.
 */
export function contentSecurityPolicyPlugin(
  readApiBaseUrl: (config: { mode: string; envDir: string }) => string | undefined = (config) => loadEnv(config.mode, config.envDir, 'VITE_').VITE_API_BASE_URL
): Plugin {
  let apiBaseUrl: string | undefined;
  return {
    name: 'content-security-policy',
    apply: 'build',
    configResolved(config) {
      // envDir is false when the build was told not to read any .env file; the variables of the environment itself are still read.
      apiBaseUrl = readApiBaseUrl({ mode: config.mode, envDir: config.envDir || config.root });
    },
    transformIndexHtml() {
      return [{ tag: 'meta', attrs: { 'http-equiv': 'Content-Security-Policy', content: contentSecurityPolicy(apiBaseUrl) }, injectTo: 'head-prepend' }];
    },
  };
}
