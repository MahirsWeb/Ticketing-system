/** Leaves the application for another address (the sign-in page of Microsoft, for instance). The one place that does it, so that a test can see where it was going. */
export function redirectTo(url: string): void {
  window.location.assign(url);
}
