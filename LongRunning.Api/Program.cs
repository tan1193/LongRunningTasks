using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors();

//builder.Services.AddHangfire(
//    config => config.UsePostgreSqlStorage(
//        options => options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("Database"))));

builder.Services.AddHangfire(configuration => configuration
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
.UseRecommendedSerializerSettings()
        .UseSqlServerStorage(Configuration.GetConnectionString("HangfireConnection")));


builder.Services.AddHangfireServer(options => options.SchedulePollingInterval = TimeSpan.FromSeconds(1));

builder.Services.AddTransient<LongRunningJob>();
builder.Services.AddTransient<LongRunningJobWithNotification>();

builder.Services.AddSignalR();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(o => o.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin().WithExposedHeaders("*"));

app.MapGet("reports/v1", async (ILogger<Program> logger) =>
{
    logger.LogInformation("Starting background job");

    await Task.Delay(TimeSpan.FromSeconds(10));

    logger.LogInformation("Completed background job");

    return "Completed";
});

app.MapPost("reports/v2", (IBackgroundJobClient backgroundJobClient) =>
{
    string jobId = backgroundJobClient.Enqueue<LongRunningJob>(job => job.ExecuteAsync(CancellationToken.None));

    return Results.AcceptedAtRoute("JobDetails", new { jobId }, jobId);
});

app.MapGet("jobs/{jobId}", (string jobId) =>
{
    var jobDetails = JobStorage.Current.GetMonitoringApi().JobDetails(jobId);

    return jobDetails.History.OrderByDescending(h => h.CreatedAt).First().StateName;
})
.WithName("JobDetails");

app.MapPost("reports/v3", async (IBackgroundJobClient backgroundJobClient, IHubContext<NotificationHub> hubContext) =>
{
    string jobId = backgroundJobClient.Enqueue<LongRunningJobWithNotification>(job => job.ExecuteAsync(CancellationToken.None));

    await hubContext.Clients.All.SendAsync("ReceiveNotification", $"Stared processing job with ID: {jobId}");

    return Results.AcceptedAtRoute("JobDetails", new { jobId }, jobId);
});

app.MapHub<NotificationHub>("notifications");

app.Run();

public class NotificationHub : Hub;

public class LongRunningJob(ILogger<LongRunningJob> logger)
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting background job");

        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

        logger.LogInformation("Completed background job");
    }
}

public class LongRunningJobWithNotification(ILogger<LongRunningJob> logger, IHubContext<NotificationHub> hubContext)
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting background job");

        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

        logger.LogInformation("Completed background job");

        await hubContext.Clients.All.SendAsync("ReceiveNotification", "Completed processing job");
    }
}
