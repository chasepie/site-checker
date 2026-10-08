# The pipeline loads the page; the Scraper recognises Known Failures

> Accepted in [design 0001](../design/0001-scraper-architecture.md); implemented in its stage 1.

The shared scrape pipeline navigates to `Site.Url` before any Scraper runs, and passes it the navigation result instead of failing on a navigation error. The Scraper, not the pipeline, decides whether what it found is a Known Failure, and may attach Requested Actions (change the VPN Location, retry). Anything it throws is an Unexpected Failure. We chose this because which states count as Known Failures varies from Site to Site and VPN Location to VPN Location, and only the Scraper knows what it expected to find. Navigating in the pipeline makes `Site.Url` decide what gets scraped and gives every kind of Scraper the same page-load handling.

## Considered Options

- **Detecting Known Failures in the pipeline**, from a built-in list or conditions declared on the Site. Rejected for the reason above; Steps Scrapers may still offer common checks as presets.
- **Letting each Scraper navigate to its own page**, as compiled Scrapers did. Most flexible, but `Site.Url` stays decorative and each kind handles page loading its own way. A Scraper can still navigate elsewhere partway through.

## Consequences

- A navigation error (a connection reset, a 403 or 429) reaches the Scraper, which can treat it as a blocked VPN Location. A Scraper that doesn't care calls `EnsureSucceeded()` to turn it into an Unexpected Failure.
- A Retry is honored once per Failing Run, so a Site that keeps hitting the same Known Failure can't retry in a loop.
