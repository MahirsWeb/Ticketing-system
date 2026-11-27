/**
 * How hours are shown, in one place. The server rounds to the same number of decimals (WorkTimeSummary.HoursDecimals in the backend) and the workbooks have
 * the number format made from it; change both together. A whole number of hours has no decimals ("2", not "2.00").
 */
export const HOURS_DECIMALS = 2;

export function formatHours(value: number): string {
  return String(Number(value.toFixed(HOURS_DECIMALS)));
}
