// Small fetch wrapper shared by all API calls. Keeps base URL resolution,
// auth header wiring, JSON parsing, and error typing in one place so the
// rest of the app never touches `fetch` directly.

const DEFAULT_BASE_URL = "https://localhost:7124";

export function getApiBaseUrl(): string {
  return import.meta.env.VITE_API_BASE_URL ?? DEFAULT_BASE_URL;
}

/**
 * Thrown for any non-2xx response. `status` is the HTTP status code and
 * `body` is the parsed JSON error payload (or raw text if parsing failed,
 * or undefined if the response had no body).
 */
export class ApiError extends Error {
  readonly status: number;
  readonly body: unknown;

  constructor(status: number, body: unknown, message?: string) {
    super(message ?? `Request failed with status ${status}`);
    this.name = "ApiError";
    this.status = status;
    this.body = body;
  }
}

export interface ApiRequestOptions {
  method?: "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
  /** Player token to send as X-Player-Token. Omit or pass null/undefined to send no auth header. */
  token?: string | null;
  body?: unknown;
  /** Query-string entries; values are url-encoded and skipped when null/undefined. */
  query?: Record<string, string | undefined | null>;
}

function safeJsonParse(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

function buildQueryString(query: Record<string, string | undefined | null>): string {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null) {
      params.set(key, value);
    }
  }
  const qs = params.toString();
  return qs.length > 0 ? `?${qs}` : "";
}

export async function apiRequest<T>(
  path: string,
  options: ApiRequestOptions = {},
): Promise<T> {
  const headers: Record<string, string> = {};
  if (options.body !== undefined) {
    headers["Content-Type"] = "application/json";
  }
  if (options.token) {
    headers["X-Player-Token"] = options.token;
  }

  const queryString = options.query ? buildQueryString(options.query) : "";

  const response = await fetch(`${getApiBaseUrl()}${path}${queryString}`, {
    method: options.method ?? "GET",
    headers,
    body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
  });

  const text = await response.text();
  const parsed = text.length > 0 ? safeJsonParse(text) : undefined;

  if (!response.ok) {
    throw new ApiError(response.status, parsed);
  }

  return parsed as T;
}
