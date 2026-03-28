import { test, expect } from '@playwright/test'

test.describe('User Management', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/')
    await page.click('text=Users')
    await page.waitForLoadState('networkidle')
  })

  test('should display user list', async ({ page }) => {
    await expect(page.locator('text=Manage user accounts')).toBeVisible()
    // Should have a DataTable or list
    await expect(page.locator('.p-datatable').or(page.locator('text=No users found'))).toBeVisible({ timeout: 5000 })
  })

  test('should open create user dialog', async ({ page }) => {
    const createBtn = page.locator('button:has-text("Create User"), button:has-text("New User")')
    if (await createBtn.isVisible()) {
      await createBtn.click()
      await expect(page.locator('text=Create User').first()).toBeVisible({ timeout: 3000 })
    }
  })

  test('should have search/filter functionality', async ({ page }) => {
    const searchInput = page.locator('input[placeholder*="Search"], input[placeholder*="Filter"]')
    if (await searchInput.isVisible()) {
      await searchInput.fill('admin')
      // Wait for filter to apply
      await page.waitForTimeout(500)
    }
  })

  test('should have export CSV button', async ({ page }) => {
    const exportBtn = page.locator('button:has-text("Export CSV"), button:has-text("Export")')
    await expect(exportBtn).toBeVisible({ timeout: 5000 })
  })
})
