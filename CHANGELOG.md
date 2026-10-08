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
