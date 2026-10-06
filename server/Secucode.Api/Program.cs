using Secucode.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSecucodeApi(builder.Configuration);

var app = builder.Build();
app.UseSecucodeApi();
app.Run();

public partial class Program;
