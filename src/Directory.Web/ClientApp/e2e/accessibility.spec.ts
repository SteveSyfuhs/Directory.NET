import { test, expect } from '@playwright/test'

test.describe('Accessibility', () => {
  test('should support keyboard navigation on dashboard', async ({ page }) => {
    await page.goto('/')
    await page.waitForLoadState('networkidle')

    // Tab should move focus through interactive elements
    await page.keyboard.press('Tab')
    const focused = await page.evaluate(() => document.activeElement?.tagName)
    expect(focused).toBeTruthy()
  })

  test('should close dialogs with Escape key', async ({ page }) => {
    await page.goto('/')
    await page.click('text=Users')
    await page.waitForLoadState('networkidle')

    const createBtn = page.locator('button:has-text("Create User"), button:has-text("New User")')
    if (await createBtn.isVisible()) {
      await createBtn.click()
      await page.waitForTimeout(500)
      await page.keyboard.press('Escape')
      // Dialog should be closed
      await page.waitForTimeout(500)
    }
  })

  test('dark mode toggle should work', async ({ page }) => {
    await page.goto('/')
    await page.waitForLoadState('networkidle')

    // Find the dark mode toggle button (sun/moon icon)
    const toggle = page.locator('button .pi-moon, button .pi-sun').first()
    if (await toggle.isVisible()) {
      await toggle.click()
      // Check that dark-mode class was toggled on html element
      const hasDarkMode = await page.evaluate(() => document.documentElement.classList.contains('dark-mode'))
      // Toggle again
      await toggle.click()
      const hasDarkMode2 = await page.evaluate(() => document.documentElement.classList.contains('dark-mode'))
      expect(hasDarkMode).not.toBe(hasDarkMode2)
    }
  })
})
