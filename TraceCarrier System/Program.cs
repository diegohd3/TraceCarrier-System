using Swashbuckle.AspNetCore.Annotations;
using TraceCarrier_System.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddScoped<ITraceabilityService, TraceabilityService>();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.EnableAnnotations();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Ok(new
{
    status = "ok",
    service = "TraceCarrier System",
    utcTime = DateTimeOffset.UtcNow
}));

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
