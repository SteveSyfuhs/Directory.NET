import { test, expect } from '@playwright/test'

test.describe('Group Management', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/')
    await page.click('text=Groups')
    await page.waitForLoadState('networkidle')
  })

  test('should display group list', async ({ page }) => {
    await expect(page.locator('text=Manage security and distribution groups')).toBeVisible()
    await expect(page.locator('.p-datatable').or(page.locator('text=No groups found'))).toBeVisible({ timeout: 5000 })
  })

  test('should open create group dialog', async ({ page }) => {
    const createBtn = page.locator('button:has-text("Create Group"), button:has-text("New Group")')
    if (await createBtn.isVisible()) {
      await createBtn.click()
      await expect(page.locator('text=Create Group').first()).toBeVisible({ timeout: 3000 })
    }
  })
})
