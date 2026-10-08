# 1.0.0

- Add database-icon filesize labels below performer ages and in studio/video footer rows.
- Keep performer card heights unchanged by reclaiming 18 pixels from their portraits.
- Format decimal KB/MB/GB/TB automatically with exact byte counts on hover.
- Sum all attributed video, image, gallery archive, audio, and text files; deduplicate shared video sources attributed through clips.
- Batch API requests, refresh active views every 30 seconds, and clean up on disable.

Validation: frontend tests, database aggregation regression tests, PostgreSQL query translation with Cove's full model, and Chromium checks of performer heights at 150/240/320 pixels.
