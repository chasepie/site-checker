# 0002: Steps Scrapers

|             |                                                                       |
| ----------- | --------------------------------------------------------------------- |
| **Status**  | Draft                                                                 |
| **Author**  | Chase Pietrangelo                                                     |
| **Created** | 2026-10-06                                                            |
| **Related** | [0001 Scraper architecture](0001-scraper-architecture.md)             |

> Status lifecycle: `Draft` → `In Review` → `Accepted` | `Rejected` | `Superseded by NNNN`.
> Once accepted, record each lasting decision as a short ADR in `docs/adr/` that links back here.

> This draft records the decisions made while writing [0001](0001-scraper-architecture.md). It builds on 0001's shared scrape pipeline, Known Failures and Requested Actions, and Test Runs, and isn't ready for review until stage 1 has shipped.

## Summary

_A Steps Scraper lets a Site be defined in the UI as an ordered list of steps (wait, extract, click, ...) instead of a C# script. Write this last._

## Motivation

From [0001](0001-scraper-architecture.md)'s goals: define a Site "via a simple set of operations that can be defined in the GUI (for example, 'navigate to this URL, wait for this selector, extract this text, take a screenshot')". A Script Scraper still needs a C# project, an IDE and an upload, which is a lot of work for a single URL.

## Goals

- _..._

## Non-goals

- _..._

## Proposed design

### Steps

A Steps Scraper is an ordered list of steps that run against the page the pipeline has already loaded from `Site.Url`. Selectors are Playwright selector strings (CSS, `text=`, `:has-text()`, `:text-matches()`), so anything a Playwright locator accepts works.

| Step                 | Parameters                                        | Behavior                                                                                                                                                                                                                                                                                      |
| -------------------- | ------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Wait for**         | selector; optional alternatives; optional timeout | Waits until the selector appears, or one of the alternatives does. Each alternative is a selector mapped to a Known Failure message and Requested Actions; if it appears first, the scrape ends as that Known Failure. If nothing appears before the timeout, the scrape fails as Unexpected. |
| **Extract**          | selector; label; optional attribute               | Reads the element's text (or the attribute), trimmed, and adds it to the content under the label.                                                                                                                                                                                             |
| **Click**            | selector                                          | Clicks the element.                                                                                                                                                                                                                                                                           |
| **Fill**             | selector; text                                    | Types the text into an input.                                                                                                                                                                                                                                                                 |
| **Press**            | key; optional selector                            | Presses a key, on the element if one is given.                                                                                                                                                                                                                                                |
| **Scroll to bottom** | none                                              | Scrolls to the bottom of the page, e.g. to trigger lazy loading.                                                                                                                                                                                                                              |
| **Delay**            | seconds                                           | Waits a fixed time.                                                                                                                                                                                                                                                                           |
| **Go to URL**        | absolute URL                                      | Navigates to another page partway through.                                                                                                                                                                                                                                                    |

Step timeouts default to Playwright's 30 s (`PlaywrightConsts.DefaultTimeoutMS`), and the whole run is still bounded by the Site's timeout.

**Known Failures** come from the alternatives on a Wait for step, which makes today's `WaitForFirstLocatorAsync` declarative. An alternative can have a grace period before it's considered, because some states only mean something once the page has had time to load. The UI offers today's two checks as presets: **Access Denied** (an `h1` with that text) and **Blank page** (a `body` with no visible children, with the same 10 s grace period as today).

**Content** is one `Label: value` line per Extract step, in step order. It's readable in a notification and compares exactly from run to run when the page hasn't changed.

**Validation** on save: at least one Extract step, unique non-empty labels, non-empty selectors, absolute `http`/`https` URLs for Go to URL, and delays shorter than the Site's timeout. Whether a selector actually matches can only be checked against a live page, which is what a Test Run is for.

**Today's Scrapers as steps.** Both are ported to Script Scrapers in stage 1 ([0001](0001-scraper-architecture.md)), but they show that steps can express the existing Sites:

| Scraper               | Steps                                                                                                                |
| --------------------- | -------------------------------------------------------------------------------------------------------------------- |
| `PiaLocationScraper`  | Extract `.exposed-card-container-info .card-info-row:nth-of-type(3) .exposed-info span:nth-of-type(2)` as `Location` |
| `BotDetectionScraper` | Wait for `div:text-matches("^Test Results:\\s*(Normal\|Robot)")`, then Extract the same selector as `Result`         |

### Data model

`ScraperDefinition` gains a nullable `Steps` payload: a complex collection, mapped to JSON like the rest of `Site.Scraper`. EF Core doesn't support inheritance for complex types, so each step is flat: a `Type` enum plus nullable parameters.

### Generated types

`Step`, `StepType` and the Wait for alternatives must be added to the lists in `ReinforcedTypingsConfiguration`.

### Frontend

In `edit-site`, a steps builder with the Known Failure presets, next to 0001's Test Run panel.

## Alternatives considered

- _..._

## Testing strategy

- _..._

## Risks and open questions

- _..._
