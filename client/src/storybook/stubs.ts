// Local type stubs for the `@storybook/react` / `@storybook/test` exports
// the stories use.
//
// WHY THIS EXISTS: the `@storybook/*` packages could not be installed in this
// environment — every `npm install` that adds new packages to the lockfile
// graph fails with npm's internal "Cannot read properties of null (reading
// 'edgesOut')" error, while installs of already-locked packages by exact
// version succeed. The stories themselves are written against Storybook's
// standard CSF3 API and are the real deliverable; this module only supplies
// the minimal type surface so the project's `tsc -b` build stays green until
// the renderer package can be installed.
//
// TO REMOVE: run `npm install --save-dev @storybook/react-vite @storybook/react @storybook/test`
// (or `npx storybook@latest init`), delete this file, and change the stories'
// import paths from "../../storybook/stubs" to "@storybook/react" /
// "@storybook/test". No story code needs to change — the shapes below match
// the real CSF3 API.

import type { ReactElement } from "react";

/** Minimal mirror of @storybook/react's `Meta` (CSF3). */
export interface Meta<Args extends object = Record<string, unknown>> {
  title: string;
  component?: unknown;
  args?: Partial<Args>;
  parameters?: Record<string, unknown>;
  tags?: string[];
}

/** Minimal mirror of @storybook/react's `StoryObj` (CSF3). */
export interface StoryObj<Args extends object = Record<string, unknown>> {
  render?: (args: Args, context: unknown) => ReactElement;
  args?: Partial<Args>;
  parameters?: Record<string, unknown>;
  play?: (context: unknown) => void | Promise<void>;
}

/** Minimal mirror of @storybook/test's `fn`. */
export function fn(impl?: (...args: unknown[]) => unknown): {
  (...args: unknown[]): unknown;
  mockClear(): void;
} {
  let implementation = impl;
  const wrapped = (...args: unknown[]) => implementation?.(...args);
  wrapped.mockClear = () => {};
  return wrapped;
}
