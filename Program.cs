using MachineKarte.Web.Repositories;
using MachineKarte.Web.Services;
using MachineKarte.Web.Services.Reporting;

using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// ─────────────────────────────────────────────────────────────
// Razor Pages
// ─────────────────────────────────────────────────────────────

builder.Services.AddRazorPages();

// ─────────────────────────────────────────────────────────────
// Monitoring (Form1)
// ─────────────────────────────────────────────────────────────

builder.Services.AddScoped<MonitoringRepository>();
builder.Services.AddScoped<MonitoringService>();

// ─────────────────────────────────────────────────────────────
// Evidence Making (Form4)
// ─────────────────────────────────────────────────────────────

builder.Services.AddScoped<EvidenceRepository>();
builder.Services.AddScoped<EvidenceMakingService>();

// ─────────────────────────────────────────────────────────────
// TE Evidence Making (Form10)
// ─────────────────────────────────────────────────────────────

builder.Services.AddScoped<TeEvidenceRepository>();
builder.Services.AddScoped<TeEvidenceMakingService>();

// ─────────────────────────────────────────────────────────────
// Action Cards (Form_Input)
// ─────────────────────────────────────────────────────────────

builder.Services.AddScoped<ActionCardRepository>();
builder.Services.AddScoped<ActionCardService>();

// ─────────────────────────────────────────────────────────────
// Raw Data (Form5)
// ─────────────────────────────────────────────────────────────

builder.Services.AddScoped<RawDataRepository>();
builder.Services.AddScoped<RawDataService>();

// ─────────────────────────────────────────────────────────────
// OCAP Viewer (Form6)
// ─────────────────────────────────────────────────────────────

builder.Services.AddScoped<OcapViewerRepository>();
builder.Services.AddScoped<OcapViewerService>();

// ─────────────────────────────────────────────────────────────
// BM Report
// ─────────────────────────────────────────────────────────────

builder.Services.AddScoped<BmReportRepository>();
builder.Services.AddScoped<BmReportService>();

// ─────────────────────────────────────────────────────────────
// MCD Monitoring (Form7)
// ─────────────────────────────────────────────────────────────

builder.Services.AddScoped<McdMonitoringRepository>();
builder.Services.AddScoped<McdMonitoringService>();

// ─────────────────────────────────────────────────────────────
// Alert Board
// ─────────────────────────────────────────────────────────────

builder.Services.AddSingleton<AlertBoardRepository>();
builder.Services.AddSingleton<AlertBoardService>();

// ─────────────────────────────────────────────────────────────
// Anti-Forgery
// ─────────────────────────────────────────────────────────────

// ─────────────────────────────────────────────────────────────
// Machine Summary
// ─────────────────────────────────────────────────────────────
builder.Services.AddScoped<MachineSummaryRepository>();
builder.Services.AddScoped<MachineSummaryService>();

// ─────────────────────────────────────────────────────────────
// Bm Analytics
// ─────────────────────────────────────────────────────────────
builder.Services.AddScoped<BmAnalyticsRepository>();
builder.Services.AddScoped<BmAnalyticsService>();

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
});

var app = builder.Build();

// ─────────────────────────────────────────────────────────────
// Middleware
// ─────────────────────────────────────────────────────────────

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// wwwroot
app.UseStaticFiles();

// Assets folder
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(builder.Environment.ContentRootPath, "Assets")),
    RequestPath = "/Assets"
});

// Generated PDF / Excel output folder
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(
            builder.Environment.ContentRootPath,
            "TempData",
            "GeneratedPdf")),
    RequestPath = "/TempData/GeneratedPdf"
});

app.UseRouting();

app.UseAntiforgery();

app.MapRazorPages();

// Default page
app.MapGet("/", context =>
{
    context.Response.Redirect("/Monitoring");
    return Task.CompletedTask;
});

app.Run();