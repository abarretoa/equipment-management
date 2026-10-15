import type { EndAllocationResult } from './types'

const isCount = (value: unknown): value is number => typeof value === 'number' && Number.isInteger(value) && value >= 0

/**
 * Whether the response really carries the return counts. It always does when
 * the API is the version this frontend was built for; an older API answers
 * the same endpoint with the plain allocation and no counts.
 */
export function hasReturnCounts(result: EndAllocationResult): boolean {
  return isCount(result.returnsCreatedCount) && isCount(result.returnsAlreadyPendingCount)
}

/**
 * Success feedback for an ended allocation. Never claims a return was
 * initiated when it already existed, and never turns a missing count into 0:
 * without trustworthy counts the message says only what is known.
 */
export function endAllocationSuccessMessage(result: EndAllocationResult): string {
  if (!result.isEndOfEngagement) return 'Alocação encerrada.'
  if (!hasReturnCounts(result)) return 'Vínculo encerrado com sucesso.'

  const created = result.returnsCreatedCount
  const alreadyPending = result.returnsAlreadyPendingCount
  if (created === 0 && alreadyPending === 0) return 'Vínculo encerrado.'
  if (created === 0) {
    return alreadyPending === 1
      ? 'Vínculo encerrado. 1 devolução já estava pendente.'
      : `Vínculo encerrado. ${alreadyPending} devoluções já estavam pendentes.`
  }

  const createdText = created === 1 ? 'Foi iniciada 1 devolução' : `Foram iniciadas ${created} devoluções`
  if (alreadyPending === 0) return `Vínculo encerrado. ${createdText}.`
  return `Vínculo encerrado. ${createdText} e ${alreadyPending === 1 ? '1 já estava pendente' : `${alreadyPending} já estavam pendentes`}.`
}
