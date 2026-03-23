using Microsoft.AspNetCore.Mvc;

namespace SiteChecker.Backend.Extensions;

public static class ControllerExtensions
{
    extension(ControllerBase controller)
    {
        public ActionResult<T> OkOrNotFound<T>(T? entity) where T : class
        {
            return entity != null ? controller.Ok(entity) : controller.NotFound();
        }
    }
}
