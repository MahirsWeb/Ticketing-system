/** Longest names the server accepts, so the fields stop the typing instead of the server refusing the result. */
export const LIMITS = { name: 25, email: 50, helpTopic: 75, category: 70, slaName: 70 } as const;

/** What the server said when it refused, or the given words when it said nothing (or could not be reached). */
export function messageOf(err: unknown, fallback: string): string {
  return (err as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback;
}
