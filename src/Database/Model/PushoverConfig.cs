using SiteChecker.Domain.Sites;

namespace SiteChecker.Database.Model;

public class PushoverConfig
{
    public PushoverPriority? SuccessPriority { get; set; } = null;
    public PushoverPriority? FailurePriority { get; set; } = null;

    public void Update(PushoverConfig notifications)
    {
        SuccessPriority = notifications.SuccessPriority;
        FailurePriority = notifications.FailurePriority;
    }
}
