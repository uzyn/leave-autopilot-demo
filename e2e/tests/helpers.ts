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

export async function logout(page: Page): Promise<void> {
  await page.getByRole("button", { name: "Log out" }).click();
  await expect(page.getByRole("link", { name: "Log in" })).toBeVisible();
}

/** A unique-per-run suffix so repeated local test runs don't collide on unique emails. */
export function uniqueSuffix(): string {
  return `${Date.now()}-${Math.floor(Math.random() * 100000)}`;
}

/** Formats a Date as the yyyy-MM-dd string expected by an <input type="date"> field. */
export function isoDate(date: Date): string {
  return date.toISOString().slice(0, 10);
}

/**
 * The next future occurrence of `dayOfWeek` (0=Sunday..6=Saturday) strictly after today, so
 * date-based E2E tests are deterministic regardless of what day they happen to run on.
 */
export function nextOccurrenceOf(dayOfWeek: number): Date {
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  let daysToAdd = (dayOfWeek - today.getDay() + 7) % 7;
  daysToAdd = daysToAdd === 0 ? 7 : daysToAdd;
  const result = new Date(today);
  result.setDate(result.getDate() + daysToAdd);
  return result;
}

function addDays(date: Date, days: number): Date {
  const result = new Date(date);
  result.setDate(result.getDate() + days);
  return result;
}

export { addDays };
