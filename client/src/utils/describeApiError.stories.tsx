// Stories for the user-facing API error copy (utils/describeApiError.ts).
//
// The wording is a UI surface in its own right — every failed request the
// app can show renders one of these strings — so it belongs in Storybook
// where it can be reviewed without triggering a real failure.
//
// NOTE ON IMPORTS: these stories use the local type stubs in
// `src/storybook/stubs.ts` only because `@storybook/*` could not be
// installed in this environment (see the comment in that file). Once
// installed, replace the import with `@storybook/react` — the story shapes
// are CSF3-standard.

import { describeApiError } from "../../utils/describeApiError";
import { ApiError } from "../../api/httpClient";
import type { StoryObj, Meta } from "../../storybook/stubs";

interface ErrorCopyStoryArgs {
  context: "move" | "create" | "load";
}

function httpError(status: number, body?: unknown): ApiError {
  return new ApiError(status, body);
}

/** Renders the copy for every failure shape the context can produce. */
function ErrorCopyTable({ context }: ErrorCopyStoryArgs) {
  const cases: Array<{ label: string; err: unknown }> = [
    { label: "400 generic", err: httpError(400) },
    { label: "401 unauthorized", err: httpError(401) },
    { label: "403 forbidden", err: httpError(403) },
    { label: "404 not found", err: httpError(404) },
    { label: "409 conflict", err: httpError(409) },
    { label: "500 server error", err: httpError(500) },
    { label: "IllegalMove body", err: httpError(400, { error: "IllegalMove", message: "That move isn't legal." }) },
    { label: "PromotionRequired body", err: httpError(400, { error: "PromotionRequired" }) },
    { label: "non-ApiError", err: new Error("network down") },
    { label: "unknown thrown value", err: "nope" },
  ];

  return (
    <div style={{ display: "grid", gap: "0.5rem", minWidth: "34rem" }}>
      <h3 style={{ margin: 0 }}>context: "{context}"</h3>
      <table>
        <thead>
          <tr>
            <th style={{ textAlign: "left" }}>Failure</th>
            <th style={{ textAlign: "left" }}>User sees</th>
          </tr>
        </thead>
        <tbody>
          {cases.map(({ label, err }) => (
            <tr key={label}>
              <td style={{ paddingRight: "1rem", whiteSpace: "nowrap" }}>{label}</td>
              <td>{describeApiError(err, context)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

const meta: Meta<ErrorCopyStoryArgs> = {
  title: "Utils/ApiErrorCopy",
  parameters: { layout: "centered" },
  args: { context: "move" },
};

export default meta;

/** Move-submission failures — the board's error line (default context). */
export const MoveContext: StoryObj<ErrorCopyStoryArgs> = {
  render: (args) => <ErrorCopyTable {...args} />,
  args: { context: "move" },
};

/**
 * Create-game failures (finding V-3): never move-voiced — a create screen
 * has no move to fail.
 */
export const CreateContext: StoryObj<ErrorCopyStoryArgs> = {
  render: (args) => <ErrorCopyTable {...args} />,
  args: { context: "create" },
};

/** Game-load failures (finding V-3): load-voiced rather than move-voiced. */
export const LoadContext: StoryObj<ErrorCopyStoryArgs> = {
  render: (args) => <ErrorCopyTable {...args} />,
  args: { context: "load" },
};
