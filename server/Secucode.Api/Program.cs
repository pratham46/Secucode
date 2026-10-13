using Secucode.Api;
using Secucode.Api.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSecucodeApi(builder.Configuration);

var app = builder.Build();
app.UseSecucodeApi();
await app.Services.InitializeDatabaseAsync();
if (args.Contains("seed"))
{
    await app.Services.SeedDemoAsync();
    return;
}
app.Run();

public partial class Program;
