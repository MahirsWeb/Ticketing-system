export interface FinishSteps {
  /** Puts files on the ticket. */
  upload: (ticketId: string, files: File[]) => Promise<unknown>;
  /** Tells the server that the e-mail has become this ticket (it copies the e-mail's own files; asking twice is harmless). */
  complete: (messageId: string, ticketId: string) => Promise<unknown>;
}

/**
 * What still has to happen after a ticket was created: its files go up, and an e-mail it came from is marked as converted. If one of
 * these fails the person presses the button again, and the ticket is not made twice (the request carries the same key), but the steps
 * used to run again in full, so the files were attached a second time. `uploaded` is what has gone up already; it is kept between
 * the attempts, so a file is sent once and a repeated attempt does only what is left.
 */
export async function finishNewTicket(
  ticketId: string,
  files: File[],
  emailMessageId: string | null,
  uploaded: Set<File>,
  steps: FinishSteps
): Promise<void> {
  const pending = files.filter((f) => !uploaded.has(f));
  if (pending.length > 0) {
    await steps.upload(ticketId, pending);
    pending.forEach((f) => uploaded.add(f));
  }
  if (emailMessageId) await steps.complete(emailMessageId, ticketId);
}
