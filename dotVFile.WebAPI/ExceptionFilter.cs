using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace dotVFile.WebAPI
{
	public class ExceptionFilter : IActionFilter, IOrderedFilter
	{
		public int Order => int.MaxValue - 10;

		public void OnActionExecuting(ActionExecutingContext context) { }

		public void OnActionExecuted(ActionExecutedContext context)
		{
			if (context.Exception != null)
			{
				var result = new Response<object?>(null, new("UNHANDLED_EXCEPTION", context.Exception.Message));

				context.Result = new ObjectResult(result)
				{
					StatusCode = (int)HttpStatusCode.InternalServerError
				};

				context.ExceptionHandled = true;
			}
		}
	}
}
