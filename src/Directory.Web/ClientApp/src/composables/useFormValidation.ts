import { ref } from 'vue'

export interface ValidationRule {
  validate: (value: any) => boolean
  message: string
}

export const required = (fieldName: string): ValidationRule => ({
  validate: (v) => v != null && String(v).trim().length > 0,
  message: `${fieldName} is required`,
})

export const maxLength = (fieldName: string, max: number): ValidationRule => ({
  validate: (v) => v == null || String(v).length <= max,
  message: `${fieldName} must be ${max} characters or fewer`,
})

export const minLength = (fieldName: string, min: number): ValidationRule => ({
  validate: (v) => v == null || String(v).trim().length === 0 || String(v).length >= min,
  message: `${fieldName} must be at least ${min} characters`,
})

export const samAccountName = (): ValidationRule => ({
  validate: (v) => v == null || /^[a-zA-Z0-9._-]{1,20}$/.test(v),
  message: 'sAMAccountName must be 1-20 alphanumeric characters, dots, hyphens, or underscores',
})

export const computerName = (): ValidationRule => ({
  validate: (v) => v == null || /^[a-zA-Z0-9-]{1,15}$/.test(v),
  message: 'Computer name must be 1-15 alphanumeric characters or hyphens',
})

export const email = (fieldName: string): ValidationRule => ({
  validate: (v) => v == null || String(v).trim().length === 0 || /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v),
  message: `${fieldName} must be a valid email address`,
})

export const matchesField = (fieldName: string, getOtherValue: () => any): ValidationRule => ({
  validate: (v) => v === getOtherValue(),
  message: `${fieldName} does not match`,
})

/**
 * Validates a single value against an array of rules.
 * Returns the first failing error message, or null if all pass.
 */
function validate(value: any, rules: ValidationRule[]): string | null {
  for (const rule of rules) {
    if (!rule.validate(value)) {
      return rule.message
    }
  }
  return null
}

/**
 * Validates multiple fields at once.
 * Takes a record mapping field names to [value, rules[]] tuples.
 * Returns a record of field name -> error message (only failing fields).
 */
function validateAll(
  fields: Record<string, [any, ValidationRule[]]>,
): Record<string, string> {
  const errors: Record<string, string> = {}
  for (const [fieldKey, [value, rules]] of Object.entries(fields)) {
    const error = validate(value, rules)
    if (error != null) {
      errors[fieldKey] = error
    }
  }
  return errors
}

/**
 * Composable for form validation.
 *
 * Provides:
 * - `errors`: reactive record of field -> error message
 * - `validate(field, value, rules)`: validate one field and store the result
 * - `validateAll(fields)`: validate all fields; returns true if there are no errors
 * - `clearErrors()`: reset all errors
 * - `hasErrors`: computed boolean
 */
export function useFormValidation() {
  const errors = ref<Record<string, string>>({})

  function validateField(field: string, value: any, rules: ValidationRule[]): string | null {
    const error = validate(value, rules)
    if (error) {
      errors.value = { ...errors.value, [field]: error }
    } else {
      const next = { ...errors.value }
      delete next[field]
      errors.value = next
    }
    return error
  }

  function validateAllFields(fields: Record<string, [any, ValidationRule[]]>): boolean {
    const result = validateAll(fields)
    errors.value = result
    return Object.keys(result).length === 0
  }

  function clearErrors() {
    errors.value = {}
  }

  function clearField(field: string) {
    const next = { ...errors.value }
    delete next[field]
    errors.value = next
  }

  return {
    errors,
    validateField,
    validateAll: validateAllFields,
    clearErrors,
    clearField,
  }
}
