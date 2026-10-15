import { z } from 'zod'

/**
 * Client-side mirror of the API's rules for the editable user fields — shared by
 * the Administration edit dialog and "Meu perfil" so both validate the same way.
 * The API stays the authority (UpdateUserRequest / UpdateProfileRequest +
 * UserManagementService). Values are sent as typed, never trimmed: the API stores
 * them as received, and its [Required] already rejects whitespace-only names.
 */
const requiredName = (message: string) =>
  z
    .string()
    .max(200, 'Máximo de 200 caracteres')
    .refine((value) => value.trim().length > 0, message)

export const editableUserFields = {
  firstName: requiredName('Indique o nome próprio'),
  lastName: requiredName('Indique o apelido'),
  phone: z.string().max(30, 'Máximo de 30 caracteres').optional(),
  address: z.string().max(500, 'Máximo de 500 caracteres').optional(),
}

/**
 * Phone is optional: an empty or blank field means "no phone" and is sent as
 * null (the API treats "", whitespace and null the same way and stores null).
 */
export function toOptionalPhone(phone: string | null | undefined): string | null {
  return phone?.trim() ? phone : null
}

/** HR profiles require an address; Administration profiles do not (docs/PROJECT_SPEC.md, section 5). */
export const HR_ADDRESS_REQUIRED_MESSAGE = 'A morada é obrigatória para utilizadores de RH'

export function isAddressMissing(address: string | null | undefined): boolean {
  return !address?.trim()
}
