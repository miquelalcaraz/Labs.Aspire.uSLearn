namespace uSLearn.Identity.Api
{
    public static class IdentityApi
    {
        public static RouteGroupBuilder MapIdentityApiV1(this IEndpointRouteBuilder app)
        {
            var api = app.MapGroup("api/identity").HasApiVersion(1.0);
            return api;
        }
    }
}
