## Storybook frontend

This package hosts a [Storybook](https://storybook.js.org/) instance for
developing and documenting UI components in isolation. Instead of copying the
entire Next.js application, it reuses components from the existing `frontend`
package and showcases them through stories.

### Usage

- `npm run storybook` – start the interactive component workshop.
- `npm run build-storybook` – generate a static Storybook build.

### Why Storybook?

Storybook lets you render individual component variations without spinning up
the whole app and capture those variations as reusable “stories”. Most teams
follow a component‑driven workflow: build each component in isolation, compose
them into more complex pieces, and only then assemble pages with real data.

