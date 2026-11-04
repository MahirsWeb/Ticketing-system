/// Text a person is writing and has not sent yet, kept in the browser tab's session storage. When the sign-in expires in the middle of
/// writing a reply the person is taken to the sign-in page; without this the reply was gone when they came back. Session storage
/// (not local) on purpose: it ends with the tab, so a shared computer does not keep somebody's unsent words.
const PREFIX = 'tst-draft:';

export function draftKey(ticketId: string, type: string) {
  return `${ticketId}:${type}`;
}

export function loadDraft(key: string): string {
  try {
    return sessionStorage.getItem(PREFIX + key) ?? '';
  } catch {
    return '';
  }
}

/// An empty or blank draft removes the entry instead of keeping an empty editor.
export function saveDraft(key: string, html: string) {
  try {
    if (!html.trim() || html === '<p></p>') sessionStorage.removeItem(PREFIX + key);
    else sessionStorage.setItem(PREFIX + key, html);
  } catch {
    // Storage can be unavailable (private window, blocked site data): the draft just is not kept.
  }
}

export function clearDraft(key: string) {
  try {
    sessionStorage.removeItem(PREFIX + key);
  } catch {
    // see saveDraft
  }
}
