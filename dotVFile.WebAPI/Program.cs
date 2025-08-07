namespace dotVFile.WebAPI
{
	public class Program
	{
		public static void Main(string[] args)
		{
			var builder = WebApplication.CreateBuilder(args);

			builder.Logging.ClearProviders();
			builder.Logging.AddConsole();

			builder.Services.AddControllers(options =>
			{
				options.Filters.Add<ExceptionFilter>();
			});

			builder.Services.AddOpenApi();

			var app = builder.Build();

			if (app.Environment.IsDevelopment())
			{
				app.MapOpenApi();

				app.UseSwaggerUI(options =>
				{
					options.SwaggerEndpoint("/openapi/v1.json", "v1");
				});

				app.UseDeveloperExceptionPage();
			}
			else
			{
				app.UseHttpsRedirection();
			}

			app.UseAuthorization();

			app.MapControllers();

			app.Run();
		}
	}
}
