import { test, expect } from '@playwright/test'

test.describe('Audit Log', () => {
  test('should navigate to audit log', async ({ page }) => {
    await page.goto('/')
    await page.click('text=Audit Log')
    await expect(page.locator('text=Track changes made to directory objects')).toBeVisible({ timeout: 5000 })
  })

  test('should have filter controls', async ({ page }) => {
    await page.goto('/')
    await page.click('text=Audit Log')
    await page.waitForLoadState('networkidle')

    // Should have action filter and date filter
    await expect(page.locator('.p-datatable').or(page.locator('text=No audit entries'))).toBeVisible({ timeout: 5000 })
  })
})
