/** Longest names the server accepts, so the fields stop the typing instead of the server refusing the result. */
export const LIMITS = { name: 25, email: 50, helpTopic: 75, category: 70, slaName: 70 } as const;

export { messageOf } from '../../lib/messageOf';
