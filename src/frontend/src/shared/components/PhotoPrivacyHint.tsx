export const PHOTO_PRIVACY_HINT =
  'Fotografe apenas o equipamento. Evite incluir pessoas, documentos, crachás ou ecrãs com informação pessoal.'

/**
 * Shown next to every condition/evidence photo upload: these photographs are
 * kept as evidence, so they should show the equipment and nothing personal.
 * Guidance only — it never blocks or changes the upload.
 */
export function PhotoPrivacyHint() {
  return <p className="text-[12px] leading-[18px] text-fg-muted">{PHOTO_PRIVACY_HINT}</p>
}
