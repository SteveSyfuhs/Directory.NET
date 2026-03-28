export interface PasswordFilter {
  name: string
  description: string
  isEnabled: boolean
  order: number
}

export interface PasswordFilterTestResult {
  isValid: boolean
  message: string
  filterResults: {
    filterName: string
    isValid: boolean
    message: string
  }[]
}
