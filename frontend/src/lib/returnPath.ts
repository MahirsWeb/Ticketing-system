/** Only an in-app path may be returned to after signing in: never an absolute URL or a protocol-relative "//host". */
export function safeReturnPath(from: unknown): string {
  return typeof from === 'string' && from.startsWith('/') && !from.startsWith('//') && !from.startsWith('/login') ? from : '/';
}
