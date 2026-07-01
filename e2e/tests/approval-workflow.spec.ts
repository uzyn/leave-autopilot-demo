import { test, expect, type Page } from "@playwright/test";
import { loginAsHr, login, logout, uniqueSuffix, isoDate, nextOccurrenceOf, addDays } from "./helpers";

const PASSWORD = "Password123!";

/** Creates a fresh Manager account via the HR admin screens. Leaves `page` logged in as HR. */
async function createManager(page: Page): Promise<{ email: string; name: string }> {
  const suffix = uniqueSuffix();
  const name = `E2E Manager ${suffix}`;
  const email = `e2e-approval-manager-${suffix}@leaveautopilot.local`;

  await page.goto("/Hr/CreateEmployee");
  await page.getByLabel("Full name").fill(name);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Role").selectOption("Manager");
  await page.getByLabel("Initial password").fill(PASSWORD);
  await page.getByRole("button", { name: "Create employee" }).click();
  await expect(page.getByRole("heading", { name: "Employees" })).toBeVisible();

  return { email, name };
}

/**
 * Creates a fresh Employee account with an Annual/Medical quota, optionally assigning it to
 * `manager`. Omitting `manager` leaves the employee manager-less, routing their requests to
 * the HR fallback queue (S5-3). Leaves `page` logged in as HR when it returns.
 */
async function createApplicantWithQuota(
  page: Page,
  quotas: { annual: number; medical: number },
  manager?: { email: string; name: string },
): Promise<{ email: string; name: string }> {
  const suffix = uniqueSuffix();
  const name = `E2E Approval Applicant ${suffix}`;
  const email = `e2e-approval-applicant-${suffix}@leaveautopilot.local`;

  await page.goto("/Hr/CreateEmployee");
  await page.getByLabel("Full name").fill(name);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Role").selectOption("Employee");
  await page.getByLabel("Initial password").fill(PASSWORD);
  await page.getByRole("button", { name: "Create employee" }).click();

  const employeeRow = page.locator("tr", { hasText: name });
  await expect(employeeRow).toBeVisible();

  if (manager) {
    await employeeRow.getByRole("link", { name: "Edit" }).click();
    await expect(page.getByRole("heading", { name: "Edit employee" })).toBeVisible();
    await page.getByLabel("Manager").selectOption({ label: `${manager.name} (${manager.email})` });
    await page.getByRole("button", { name: "Save changes" }).click();
    await expect(page.getByRole("heading", { name: "Employees" })).toBeVisible();
  }

  const row = page.locator("tr", { hasText: name });
  await row.getByRole("link", { name: "Quotas" }).click();
  await expect(page.getByRole("heading", { name: /quotas/i })).toBeVisible();
  await page.getByLabel("Annual (days)").fill(String(quotas.annual));
  await page.getByLabel("Medical (days)").fill(String(quotas.medical));
  await page.getByRole("button", { name: "Save quotas" }).click();

  return { email, name };
}

async function submitOneDayAnnualRequest(page: Page, date: Date): Promise<void> {
  await page.goto("/Leave/Apply");
  await page.getByLabel("Leave type").selectOption("Annual");
  await page.getByLabel("Start date", { exact: true }).fill(isoDate(date));
  await page.getByLabel("End date", { exact: true }).fill(isoDate(date));
  await page.getByRole("button", { name: "Submit request" }).click();
  await expect(page.getByRole("heading", { name: "My leave" })).toBeVisible();
}

/**
 * S5-4 acceptance criteria: browser-based coverage of the manager/HR approval flow added in
 * Sprint 5 (S5-1 approval queue, S5-2 approve/reject decisions, S5-3 HR fallback for
 * manager-less users).
 */
test.describe("Approval workflow", () => {
  test("manager approves a report's pending request and the requester's balance/history reflects the decision", async ({
    page,
  }) => {
    await loginAsHr(page);
    const manager = await createManager(page);
    const employee = await createApplicantWithQuota(page, { annual: 14, medical: 14 }, manager);
    await logout(page);

    await login(page, employee.email, PASSWORD);
    const monday = nextOccurrenceOf(1);
    await submitOneDayAnnualRequest(page, monday);
    await expect(page.locator("#remaining-Annual")).toHaveText("13");
    await logout(page);

    await login(page, manager.email, PASSWORD);
    await page.goto("/Approval");
    const queueRow = page.locator("#manager-queue tr", { hasText: employee.name });
    await expect(queueRow).toBeVisible();
    await expect(queueRow.getByText("Annual")).toBeVisible();
    await queueRow.getByRole("button", { name: "Approve" }).click();
    await expect(page.getByText("Request approved.")).toBeVisible();
    await logout(page);

    await login(page, employee.email, PASSWORD);
    await page.goto("/Leave/Index");
    await expect(page.locator("span.badge", { hasText: "Approved" })).toBeVisible();
    // Balance unchanged from the Pending reservation: Approve confirms it, doesn't re-deduct.
    await expect(page.locator("#remaining-Annual")).toHaveText("13");
  });

  test("manager rejects a request with a note; requester sees the rejection and released balance", async ({ page }) => {
    await loginAsHr(page);
    const manager = await createManager(page);
    const employee = await createApplicantWithQuota(page, { annual: 14, medical: 14 }, manager);
    await logout(page);

    await login(page, employee.email, PASSWORD);
    const monday = nextOccurrenceOf(1);
    const tuesday = addDays(monday, 1);
    await page.goto("/Leave/Apply");
    await page.getByLabel("Leave type").selectOption("Annual");
    await page.getByLabel("Start date", { exact: true }).fill(isoDate(monday));
    await page.getByLabel("End date", { exact: true }).fill(isoDate(tuesday));
    await page.getByRole("button", { name: "Submit request" }).click();
    await expect(page.locator("#remaining-Annual")).toHaveText("12"); // 14 - 2 reserved
    await logout(page);

    await login(page, manager.email, PASSWORD);
    await page.goto("/Approval");
    const queueRow = page.locator("#manager-queue tr", { hasText: employee.name });
    await expect(queueRow).toBeVisible();
    await queueRow.getByPlaceholder("Optional note").fill("Team is short-staffed that week.");
    await queueRow.getByRole("button", { name: "Reject" }).click();
    await expect(page.getByText("Request rejected.")).toBeVisible();
    await logout(page);

    await login(page, employee.email, PASSWORD);
    await page.goto("/Leave/Index");
    await expect(page.locator("span.badge", { hasText: "Rejected" })).toBeVisible();
    await expect(page.getByText("Team is short-staffed that week.")).toBeVisible();
    await expect(page.locator("#remaining-Annual")).toHaveText("14"); // fully released
  });

  test("a manager-less user's request surfaces in the HR fallback queue and HR can decide it", async ({ page }) => {
    await loginAsHr(page);
    const employee = await createApplicantWithQuota(page, { annual: 14, medical: 14 }); // no manager assigned
    await logout(page);

    await login(page, employee.email, PASSWORD);
    const monday = nextOccurrenceOf(1);
    await submitOneDayAnnualRequest(page, monday);
    await logout(page);

    await loginAsHr(page);
    await page.goto("/Approval");
    await expect(page.getByRole("heading", { name: "Manager-less requests (HR fallback)" })).toBeVisible();
    const fallbackRow = page.locator("#hr-fallback-queue tr", { hasText: employee.name });
    await expect(fallbackRow).toBeVisible();
    await fallbackRow.getByRole("button", { name: "Approve" }).click();
    await expect(page.getByText("Request approved.")).toBeVisible();
    await logout(page);

    await login(page, employee.email, PASSWORD);
    await page.goto("/Leave/Index");
    await expect(page.locator("span.badge", { hasText: "Approved" })).toBeVisible();
  });
});
