import { expect, type Page } from "@playwright/test";

/**
 * Seed accounts created by DataSeeder when `Seed:IncludeSampleData` is true — the case for
 * the `app` service in docker-compose.yml (ASPNETCORE_ENVIRONMENT=Development). See
 * README's "Seed data" section for the full list.
 */
export const HR_EMAIL = process.env.SEED_HR_EMAIL ?? "hr@leaveautopilot.local";
export const HR_PASSWORD = process.env.SEED_HR_PASSWORD ?? "ChangeMe123!";
export const SAMPLE_EMPLOYEE_EMAIL = "alice@leaveautopilot.local";
export const SAMPLE_EMPLOYEE_PASSWORD = "Password123!";

export async function login(page: Page, email: string, password: string): Promise<void> {
  await page.goto("/Account/Login");
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Log in" }).click();
  await expect(page.getByText(/Welcome/)).toBeVisible();
}

export async function loginAsHr(page: Page): Promise<void> {
  await login(page, HR_EMAIL, HR_PASSWORD);
}

/** A unique-per-run suffix so repeated local test runs don't collide on unique emails. */
export function uniqueSuffix(): string {
  return `${Date.now()}-${Math.floor(Math.random() * 100000)}`;
}
