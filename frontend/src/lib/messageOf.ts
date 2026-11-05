/** What the server said when it refused, or the given words when it said nothing (or could not be reached). */
export function messageOf(err: unknown, fallback: string): string {
  return (err as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback;
}
