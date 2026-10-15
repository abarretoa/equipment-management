/**
 * The audit API filters `OccurredAtUtc >= fromUtc` and `<= toUtc` on a
 * `timestamptz` column, binding each as a .NET DateTime. A bare calendar date
 * ("2026-10-10") binds with Kind=Unspecified, which Npgsql refuses for
 * `timestamptz` (the request fails); an ISO instant with "Z" binds as UTC.
 *
 * The date pickers are calendar days in the user's own timezone (the table
 * shows times in that timezone too), so each day is sent as the UTC instant of
 * its local start, or of its local last millisecond — "Data final" includes the
 * whole selected day. Built from local date parts, so DST days are correct.
 */
function localDayParts(date: string): [number, number, number] | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(date)
  return match ? [Number(match[1]), Number(match[2]) - 1, Number(match[3])] : null
}

export function startOfLocalDayAsUtc(date: string): string | undefined {
  const parts = localDayParts(date)
  return parts ? new Date(parts[0], parts[1], parts[2], 0, 0, 0, 0).toISOString() : undefined
}

export function endOfLocalDayAsUtc(date: string): string | undefined {
  const parts = localDayParts(date)
  return parts ? new Date(parts[0], parts[1], parts[2], 23, 59, 59, 999).toISOString() : undefined
}
