# 0003: Prompt Scrapers

|             |                                                                       |
| ----------- | --------------------------------------------------------------------- |
| **Status**  | Draft                                                                 |
| **Author**  | Chase Pietrangelo                                                     |
| **Created** | 2026-10-06                                                            |
| **Related** | [0001 Scraper architecture](0001-scraper-architecture.md), [0002 Steps Scrapers](0002-steps-scrapers.md)|

> Status lifecycle: `Draft` → `In Review` → `Accepted` | `Rejected` | `Superseded by NNNN`.
> Once accepted, record each lasting decision as a short ADR in `docs/adr/` that links back here.

> This draft records the decisions made while writing [0001](0001-scraper-architecture.md). It builds on 0001's shared scrape pipeline, Known Failures and Requested Actions, and isn't ready for review until stage 1 has shipped.

## Summary

_A Prompt Scraper lets a Site be defined by a prompt: an LLM drives the page on every Site Check and returns the content. Write this last._

## Motivation

From [0001](0001-scraper-architecture.md)'s goals: define a Site "by using a prompt that can be passed to an LLM that will perform the scrape using something like the Playwright MCP server."

## Goals

- _..._

## Non-goals

- _..._

## Proposed design

The LLM scrapes on every Site Check. The executor gives it the Site's prompt and access to the page the pipeline loaded, and the model ends the run with one structured result: content, or a Known Failure with a message and Requested Actions. Like every executor, it runs under the Site's timeout, which an LLM-driven Site will usually need to raise.

## Alternatives considered

### LLM authors Steps Scrapers instead of scraping every check

The LLM would explore the page once and produce a Steps Scraper for review, after which checks run deterministically with no per-check cost. It isn't the chosen role because per-check scraping handles pages that steps can't describe. It remains a possible future expansion alongside per-check scraping.

## Testing strategy

- _..._

## Risks and open questions

- **How the model drives the page.** In-process tools wrapping the pipeline's `IPage`, or the Playwright MCP server pointed at Browserless. Not decided yet.
- **Stable content.** Updated notifications compare content exactly, so the output has to be identical when the page hasn't changed. This is the main risk of this kind.
- Model choice, API key configuration, and the cost and latency of a model call on every check.
