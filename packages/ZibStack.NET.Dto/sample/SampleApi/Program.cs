using ZibStack.NET.Dto;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new PatchFieldJsonConverterFactory()));
var app = builder.Build();
app.MapControllers();
app.Run();
