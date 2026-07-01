import { test, expect, type Page } from "@playwright/test";
import { loginAsHr, login, logout, uniqueSuffix, isoDate, nextOccurrenceOf, addDays } from "./helpers";

const EMPLOYEE_PASSWORD = "Password123!";

/**
 * Creates a fresh employee via the HR admin screens (S3-1) and sets their Annual/Medical
 * quotas (S3-3) for the current year, so each test gets an isolated balance rather than
 * sharing/depleting the shared dev sample-data accounts (alice/bob) across repeated runs.
 * Leaves `page` logged in as HR when it returns.
 */
async function createEmployeeWithQuota(
  page: Page,
  quotas: { annual: number; medical: number },
): Promise<{ email: string; name: string }> {
  const suffix = uniqueSuffix();
  const name = `E2E Leave Applicant ${suffix}`;
  const email = `e2e-leave-${suffix}@leaveautopilot.local`;

  await page.goto("/Hr/CreateEmployee");
  await page.getByLabel("Full name").fill(name);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Role").selectOption("Employee");
  await page.getByLabel("Initial password").fill(EMPLOYEE_PASSWORD);
  await page.getByRole("button", { name: "Create employee" }).click();

  const employeeRow = page.locator("tr", { hasText: name });
  await expect(employeeRow).toBeVisible();
  await employeeRow.getByRole("link", { name: "Quotas" }).click();

  await expect(page.getByRole("heading", { name: /quotas/i })).toBeVisible();
  await page.getByLabel("Annual (days)").fill(String(quotas.annual));
  await page.getByLabel("Medical (days)").fill(String(quotas.medical));
  await page.getByRole("button", { name: "Save quotas" }).click();

  return { email, name };
}

/**
 * S4-4 acceptance criteria: browser-based coverage of the leave application flow (S4-1
 * working-day/half-day calc, S4-2 balance reservation, S4-3 submission) added in Sprint 4.
 */
test.describe("Leave application", () => {
  test("employee submits an Annual request spanning a weekend and sees the correct chargeable-day count and reserved balance", async ({
    page,
  }) => {
    await loginAsHr(page);
    const { email } = await createEmployeeWithQuota(page, { annual: 14, medical: 14 });
    await logout(page);

    await login(page, email, EMPLOYEE_PASSWORD);

    const friday = nextOccurrenceOf(5); // Friday
    const monday = addDays(friday, 3); // the following Monday

    await page.goto("/Leave/Apply");
    await page.getByLabel("Leave type").selectOption("Annual");
    await page.getByLabel("Start date", { exact: true }).fill(isoDate(friday));
    await page.getByLabel("End date", { exact: true }).fill(isoDate(monday));
    await page.getByRole("button", { name: "Submit request" }).click();

    // Weekend excluded: Friday + Monday = 2 chargeable days, reserved from the balance.
    await expect(page.getByRole("heading", { name: "My leave" })).toBeVisible();
    await expect(page.getByText("Submitted Annual request for 2 day(s)")).toBeVisible();
    await expect(page.locator("#remaining-Annual")).toHaveText("12");
    await expect(page.locator("span.badge", { hasText: "Pending" })).toBeVisible();
  });

  test("employee submission is blocked in the UI with a clear message for insufficient balance", async ({ page }) => {
    await loginAsHr(page);
    const { email } = await createEmployeeWithQuota(page, { annual: 1, medical: 1 });
    await logout(page);

    await login(page, email, EMPLOYEE_PASSWORD);

    const monday = nextOccurrenceOf(1);
    const friday = addDays(monday, 4);

    await page.goto("/Leave/Apply");
    await page.getByLabel("Leave type").selectOption("Annual");
    await page.getByLabel("Start date", { exact: true }).fill(isoDate(monday));
    await page.getByLabel("End date", { exact: true }).fill(isoDate(friday));
    await page.getByRole("button", { name: "Submit request" }).click();

    // Mon-Fri = 5 chargeable days, but only 1 day remains in the quota — blocked, not submitted.
    await expect(page.getByText(/Insufficient Annual balance/)).toBeVisible();
    await expect(page).toHaveURL(/\/Leave\/Apply$/);
  });

  test("employee submission is blocked in the UI with a clear message for a cross-year span", async ({ page }) => {
    await loginAsHr(page);
    const { email } = await createEmployeeWithQuota(page, { annual: 14, medical: 14 });
    await logout(page);

    await login(page, email, EMPLOYEE_PASSWORD);

    // Far enough in the future that it's never mistaken for a past-date rejection.
    const nextYear = new Date().getFullYear() + 2;

    await page.goto("/Leave/Apply");
    await page.getByLabel("Leave type").selectOption("Annual");
    await page.getByLabel("Start date", { exact: true }).fill(`${nextYear}-12-30`);
    await page.getByLabel("End date", { exact: true }).fill(`${nextYear + 1}-01-02`);
    await page.getByRole("button", { name: "Submit request" }).click();

    await expect(page.getByText("Leave requests cannot span across calendar years.")).toBeVisible();
    await expect(page).toHaveURL(/\/Leave\/Apply$/);
  });
});
