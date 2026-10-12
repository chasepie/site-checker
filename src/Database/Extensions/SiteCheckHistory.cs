using Microsoft.EntityFrameworkCore;
using SiteChecker.Database.Model;

namespace SiteChecker.Database.Extensions;

/// <summary>
/// The Succeeded check that set a Site's Baseline.
/// </summary>
public sealed record BaselineCheck(int Id, DateTime CompletedDate, string? Value);

/// <summary>
/// A Site's history up to a point: the latest Succeeded check before it (the Baseline), and the
/// Failed checks completed since, which make up the Failing Run so far.
/// </summary>
/// <param name="Baseline">The latest Succeeded check, or <c>null</c> if the Site has none.</param>
/// <param name="FailingRun">A query for the Failed checks completed after <paramref name="Baseline"/>.</param>
public sealed record FailingRunSoFar(BaselineCheck? Baseline, IQueryable<SiteCheck> FailingRun);

public static class SiteCheckHistory
{
    extension(IQueryable<SiteCheck> siteChecks)
    {
        /// <summary>
        /// The Site's Baseline and Failing Run as of a point in its history. History is ordered by
        /// when checks completed (<see cref="SiteCheck.CompletedDate"/>, then ID), not by when they
        /// were created: a Baseline Reset recorded while a check is open completes first (see
        /// <c>docs/adr/0002-runner-triggers-notifications.md</c>).
        /// </summary>
        /// <param name="siteId">The Site.</param>
        /// <param name="completedDate">The point in history; only checks completed before it count.</param>
        /// <param name="siteCheckId">Breaks ties with checks completed at the same instant.</param>
        /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
        public async Task<FailingRunSoFar> FailingRunBeforeAsync(
            int siteId,
            DateTime completedDate,
            int siteCheckId,
            CancellationToken cancellationToken)
        {
            var completedBefore = siteChecks.Where(sc => sc.SiteId == siteId
                && (sc.CompletedDate < completedDate || (sc.CompletedDate == completedDate && sc.Id < siteCheckId)));

            var baseline = await completedBefore
                .Where(sc => sc.Status == CheckStatus.Succeeded)
                .OrderByDescending(sc => sc.CompletedDate)
                .ThenByDescending(sc => sc.Id)
                .Select(sc => new BaselineCheck(sc.Id, sc.CompletedDate!.Value, sc.Value))
                .FirstOrDefaultAsync(cancellationToken);

            var failingRun = completedBefore.Where(sc => sc.Status == CheckStatus.Failed);
            if (baseline != null)
            {
                failingRun = failingRun.Where(sc =>
                    sc.CompletedDate > baseline.CompletedDate
                    || (sc.CompletedDate == baseline.CompletedDate && sc.Id > baseline.Id));
            }

            return new FailingRunSoFar(baseline, failingRun);
        }
    }
}
