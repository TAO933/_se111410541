import { test, expect } from "@playwright/test";

test.use({ baseURL: "http://localhost:3000" });

test("1. 首頁正確載入", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByTestId("page-title")).toHaveText("我的部落格");
  await expect(page.getByTestId("post-form")).toBeVisible();
});

test("2. 發表新文章會出現在列表", async ({ page }) => {
  await page.goto("/");

  const unique = `E2E 測試文章 ${Date.now()}`;
  await page.getByTestId("input-title").fill(unique);
  await page.getByTestId("input-author").fill("E2E 機器人");
  await page.getByTestId("input-body").fill("這是 Playwright 自動化測試寫入的內容");
  await page.getByTestId("submit-btn").click();

  const newItem = page.getByTestId("post-item").filter({ hasText: unique });
  await expect(newItem).toBeVisible();
});

test("3. 標題沒填時表單不能送出", async ({ page }) => {
  await page.goto("/");
  await page.getByTestId("input-body").fill("只有內容");
  await page.getByTestId("submit-btn").click();
  // HTML required 屬性會讓瀏覽器擋下送出
  await expect(page.getByTestId("input-title")).toBeFocused();
});

test("4. 建立的內容離開頁面再回來仍然存在", async ({ page }) => {
  await page.goto("/");
  const unique = `持久性測試 ${Date.now()}`;
  await page.getByTestId("input-title").fill(unique);
  await page.getByTestId("input-body").fill("重新載入後應該還在");
  await page.getByTestId("submit-btn").click();

  await expect(page.getByTestId("post-list").getByText(unique)).toBeVisible();

  await page.reload();
  await expect(page.getByTestId("post-list").getByText(unique)).toBeVisible();
});
