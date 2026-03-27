import { expect, test } from "@playwright/test";

test("KC-0108 smoke: desk shell is visible and chat input is interactive", async ({ page }) => {
  await page.goto("/", { waitUntil: "domcontentloaded" });

  await expect(page).toHaveTitle("KodaClaw");
  await expect(page.getByTestId("kc-shell")).toBeVisible();

  const input = page.getByTestId("chat-input");
  await expect(input).toBeVisible();
  await input.fill("smoke: hello from playwright");
  await expect(input).toHaveValue("smoke: hello from playwright");

  await expect(page.getByTestId("chat-stream")).toBeVisible();
});
