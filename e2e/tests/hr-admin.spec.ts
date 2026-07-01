import { test, expect } from "@playwright/test";
import { loginAsHr, login, SAMPLE_EMPLOYEE_EMAIL, SAMPLE_EMPLOYEE_PASSWORD, uniqueSuffix } from "./helpers";

/**
 * S3-4 acceptance criteria: browser-based coverage of the HR administration flows added in
 * Sprint 3 (S3-1 employee accounts, S3-2 manager assignment, S3-3 annual quotas), plus a
 * negative test proving HR-only screens are unreachable by a non-HR role.
 */
test.describe("HR administration", () => {
  test("HR creates an employee, assigns a manager, and sets Annual/Medical quotas", async ({ page }) => {
    await loginAsHr(page);

    const suffix = uniqueSuffix();
    const managerName = `E2E Manager ${suffix}`;
    const managerEmail = `e2e-manager-${suffix}@leaveautopilot.local`;
    const employeeName = `E2E Employee ${suffix}`;
    const employeeEmail = `e2e-employee-${suffix}@leaveautopilot.local`;

    // Create the manager first so it's assignable to the employee below.
    await page.goto("/Hr/CreateEmployee");
    await page.getByLabel("Full name").fill(managerName);
    await page.getByLabel("Email").fill(managerEmail);
    await page.getByLabel("Role").selectOption("Manager");
    await page.getByLabel("Initial password").fill("Password123!");
    await page.getByRole("button", { name: "Create employee" }).click();

    await expect(page.getByRole("heading", { name: "Employees" })).toBeVisible();
    await expect(page.getByRole("cell", { name: managerName })).toBeVisible();

    // S3-1: create the employee account.
    await page.goto("/Hr/CreateEmployee");
    await page.getByLabel("Full name").fill(employeeName);
    await page.getByLabel("Email").fill(employeeEmail);
    await page.getByLabel("Role").selectOption("Employee");
    await page.getByLabel("Initial password").fill("Password123!");
    await page.getByRole("button", { name: "Create employee" }).click();

    const employeeRow = page.locator("tr", { hasText: employeeName });
    await expect(employeeRow).toBeVisible();
    await expect(employeeRow.getByText("—")).toBeVisible(); // no manager assigned yet

    // S3-2: assign the manager.
    await employeeRow.getByRole("link", { name: "Edit" }).click();
    await expect(page.getByRole("heading", { name: "Edit employee" })).toBeVisible();
    await page.getByLabel("Manager").selectOption({ label: `${managerName} (${managerEmail})` });
    await page.getByRole("button", { name: "Save changes" }).click();

    await expect(page.getByRole("heading", { name: "Employees" })).toBeVisible();
    const updatedEmployeeRow = page.locator("tr", { hasText: employeeName });
    await expect(updatedEmployeeRow.getByText(managerName)).toBeVisible();

    // S3-3: set Annual and Medical quotas for the current year.
    await updatedEmployeeRow.getByRole("link", { name: "Quotas" }).click();
    await expect(page.getByRole("heading", { name: /quotas/i })).toBeVisible();
    await page.getByLabel("Annual (days)").fill("18");
    await page.getByLabel("Medical (days)").fill("10");
    await page.getByRole("button", { name: "Save quotas" }).click();

    await expect(page.locator("#AnnualAllocatedDays")).toHaveValue("18.0");
    await expect(page.locator("#MedicalAllocatedDays")).toHaveValue("10.0");
    await expect(page.getByText(/Updated \d{4} quotas/)).toBeVisible();
  });

  test("a non-HR user is forbidden from HR employee-administration screens", async ({ page }) => {
    await login(page, SAMPLE_EMPLOYEE_EMAIL, SAMPLE_EMPLOYEE_PASSWORD);

    // Navigation shouldn't offer the HR-only "Employees" link to a non-HR user.
    await expect(page.getByRole("link", { name: "Employees" })).toHaveCount(0);

    const response = await page.goto("/Hr/Employees");
    expect(response?.status()).toBe(403);
  });
});
