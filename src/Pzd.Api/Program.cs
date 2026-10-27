var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/", () => "PZD API");

app.Run();
