import { expect, test } from "@playwright/test";

test("KC-0108 smoke: desk shell is visible and chat input is interactive", async ({ page }) => {
  await page.goto("/", { waitUntil: "domcontentloaded" });

  await expect(page).toHaveTitle("KodaClaw 现场中枢");
  await expect(page.getByRole("heading", { name: "现场中枢" })).toBeVisible();
  await expect(page.getByTestId(/bootstrap-shell|main-shell/)).toBeVisible();

  const input = page.getByTestId("chat-input");
  await expect(input).toBeVisible();
  await input.fill("smoke: hello from playwright");
  await expect(input).toHaveValue("smoke: hello from playwright");

  await expect(page.getByTestId("chat-stream")).toBeVisible();
});
