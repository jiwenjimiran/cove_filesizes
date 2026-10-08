# 1.2.0

- Replace the Filesize comparison/text controls with numerical Minimum (inclusive) and Maximum (inclusive) inputs, each with an independent MB/GB dropdown.
- Empty minimum means 0; empty maximum means unlimited. Both empty leaves the list unrestricted by size.
- Preserve filtering across all supported lists and drilldowns, including summed performer/studio totals, native pagination, and permissions.

Validation: real React controlled-input integration, clearing/reopening and cleanup checks, Chromium range-control visibility/responsive checks, inclusive/open-bound database regressions, and existing card/backend tests.

# 1.1.0

- Add Filesize filters to videos, performers, studios, images, galleries, audios, and texts, including drilldown lists.
- Compare total recorded bytes using Equals, Not Equals, Greater Than, or Less Than, with decimal KB/MB/GB/TB input. Equals and Not Equals also accept inclusive ranges such as `500 MB..10 GB`.
- Apply filtering before native pagination while preserving other filters, ordering, counts, permissions, and drilldown scope. Performer/studio filters use the same attributed-file totals as their card labels.
- On Cove 1.5.1, Filesize must be a top-level filter; nested advanced condition groups return an explanatory error.

Validation: database comparisons for all seven entity/media kinds, unit and range validation, request middleware/authentication/permission checks, native performer/studio pagination against a read-only live library, PostgreSQL translation against Cove's full model, and existing card selection/layout regression checks.

# 1.0.2

- Keep filesize labels visible when video, performer, or studio cards are selected.
- Identify cards through hidden public extension-slot context, including cards loaded while selection mode is already active.
- Preserve known identities for embedded cards while Cove temporarily removes their navigation links.

Validation: selection/deselection and selected-card recycling regression tests for all three card kinds, browser visibility and performer-height checks, and backend aggregation tests.

# 1.0.1

- Fix missing performer and studio filesize labels on large libraries: the original totals queries could time out before returning any labels.
- Filter attribution first and aggregate matching files across the entire batch instead of rescanning the library separately for each card.
- Preserve multi-file and multi-media totals, clip source deduplication, zero totals, and the existing card layouts.

Validation: frontend and browser layout checks; mixed-entity database regression tests; PostgreSQL translation with Cove's full model; read-only checks on a running Cove 1.5.1 library with over one million files. Batches of 40 performers and 40 studios returned in approximately 240 ms and 150 ms, respectively.

# 1.0.0

- Add database-icon filesize labels below performer ages and in studio/video footer rows.
- Keep performer card heights unchanged by reclaiming 18 pixels from their portraits.
- Format decimal KB/MB/GB/TB automatically with exact byte counts on hover.
- Sum all attributed video, image, gallery archive, audio, and text files; deduplicate shared video sources attributed through clips.
- Batch API requests, refresh active views every 30 seconds, and clean up on disable.

Validation: frontend tests, database aggregation regression tests, PostgreSQL query translation with Cove's full model, and Chromium checks of performer heights at 150/240/320 pixels.
