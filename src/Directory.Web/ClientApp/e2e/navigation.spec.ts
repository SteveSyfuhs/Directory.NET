import { test, expect } from '@playwright/test'

test.describe('Navigation', () => {
  test('should load the dashboard', async ({ page }) => {
    await page.goto('/')
    await expect(page).toHaveTitle(/Directory/)
    // Look for dashboard content
    await expect(page.locator('text=Active Directory environment overview').or(page.locator('text=Dashboard'))).toBeVisible({ timeout: 10000 })
  })

  test('should navigate to Users view', async ({ page }) => {
    await page.goto('/')
    // Click Users in sidebar nav
    await page.click('text=Users')
    await expect(page.locator('text=Manage user accounts')).toBeVisible({ timeout: 5000 })
  })

  test('should navigate to Groups view', async ({ page }) => {
    await page.goto('/')
    await page.click('text=Groups')
    await expect(page.locator('text=Manage security and distribution groups')).toBeVisible({ timeout: 5000 })
  })

  test('should navigate to Computers view', async ({ page }) => {
    await page.goto('/')
    await page.click('text=Computers')
    await expect(page.locator('text=Manage computer accounts')).toBeVisible({ timeout: 5000 })
  })

  test('should navigate to Browse view', async ({ page }) => {
    await page.goto('/')
    await page.click('text=Browse Directory')
    await expect(page.locator('text=Browse the directory tree').or(page.locator('text=Directory Browser'))).toBeVisible({ timeout: 5000 })
  })
})
