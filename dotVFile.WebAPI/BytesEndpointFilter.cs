
namespace dotVFile.WebAPI
{
	public class BytesEndpointFilter : IEndpointFilter
	{
		public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
		{
			return await next(context);
		}
	}
}
