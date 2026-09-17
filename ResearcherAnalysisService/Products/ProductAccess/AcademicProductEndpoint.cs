using Microsoft.AspNetCore.Mvc;

namespace ResearcherAnalysisService.Products.ProductAccess;

public static class AcademicProductEndpoint
{
    public static ActionResult Error(Exception exception) => exception switch
    {
        AcademicProductAccessUnavailableException => new ObjectResult(new
        {
            Message = "Product authorization is not configured."
        }) { StatusCode = StatusCodes.Status503ServiceUnavailable },
        AcademicProductUnauthenticatedException => new UnauthorizedObjectResult(new
        {
            Message = "Authentication is required."
        }),
        AcademicProductAccessDeniedException => new NotFoundObjectResult(new
        {
            Message = "The requested resource was not found."
        }),
        _ => throw exception
    };
}
