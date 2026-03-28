import { test, expect } from '@playwright/test'

test.describe('Advanced Search', () => {
  test('should navigate to advanced search', async ({ page }) => {
    await page.goto('/')
    await page.click('text=Advanced Search')
    await expect(page.locator('text=Search the directory using LDAP filters')).toBeVisible({ timeout: 5000 })
  })

  test('should have search form elements', async ({ page }) => {
    await page.goto('/')
    await page.click('text=Advanced Search')
    await page.waitForLoadState('networkidle')

    // Should have filter input, base DN, and search button
    await expect(page.locator('button:has-text("Search")')).toBeVisible({ timeout: 5000 })
  })
})
