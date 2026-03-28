using Directory.Web.Services;

namespace Directory.Web.Endpoints;

public static class AccessReviewEndpoints
{
    public static RouteGroupBuilder MapAccessReviewEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (AccessReviewService svc) =>
        {
            var reviews = await svc.GetAllReviewsAsync();
            return Results.Ok(reviews);
        })
        .WithName("ListAccessReviews")
        .WithTags("AccessReviews");

        group.MapPost("/", async (AccessReview review, AccessReviewService svc) =>
        {
            var created = await svc.CreateReviewAsync(review);
            return Results.Created($"/api/v1/access-reviews/{created.Id}", created);
        })
        .WithName("CreateAccessReview")
        .WithTags("AccessReviews");

        group.MapGet("/{id}", async (string id, AccessReviewService svc) =>
        {
            var review = await svc.GetReviewAsync(id);
            return review is null ? Results.NotFound() : Results.Ok(review);
        })
        .WithName("GetAccessReview")
        .WithTags("AccessReviews");

        group.MapPost("/{id}/start", async (string id, AccessReviewService svc) =>
        {
            var review = await svc.StartReviewAsync(id);
            return review is null ? Results.NotFound() : Results.Ok(review);
        })
        .WithName("StartAccessReview")
        .WithTags("AccessReviews");

        group.MapGet("/{id}/decisions", async (string id, AccessReviewService svc) =>
        {
            var decisions = await svc.GetDecisionsAsync(id);
            return Results.Ok(decisions);
        })
        .WithName("GetAccessReviewDecisions")
        .WithTags("AccessReviews");

        group.MapPost("/{id}/decisions", async (string id, AccessReviewDecision decision, AccessReviewService svc) =>
        {
            decision.ReviewId = id;
            var result = await svc.SubmitDecisionAsync(id, decision);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("SubmitAccessReviewDecision")
        .WithTags("AccessReviews");

        group.MapPost("/{id}/complete", async (string id, AccessReviewService svc) =>
        {
            var review = await svc.CompleteReviewAsync(id);
            return review is null ? Results.NotFound() : Results.Ok(review);
        })
        .WithName("CompleteAccessReview")
        .WithTags("AccessReviews");

        group.MapGet("/pending", async (string reviewerDn, AccessReviewService svc) =>
        {
            var reviews = await svc.GetPendingReviewsAsync(reviewerDn ?? "");
            return Results.Ok(reviews);
        })
        .WithName("GetPendingAccessReviews")
        .WithTags("AccessReviews");

        return group;
    }
}
