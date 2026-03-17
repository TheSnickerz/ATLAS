using Microsoft.EntityFrameworkCore;
using ATLAS.Data;
using ATLAS.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<AtlasContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=cmdb.db"));

// NVD sync — typed HttpClient with a generous timeout for large paginated syncs
builder.Services.AddHttpClient<NvdSyncService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
    client.DefaultRequestHeaders.Add("User-Agent", "ATLAS/1.0");
});

// Singleton tracks live sync progress across the background task and polling requests
builder.Services.AddSingleton<SyncProgressTracker>();

// Greenbone integration
builder.Services.AddSingleton<GreenboneSyncTracker>();
builder.Services.AddTransient<GreenboneService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AtlasContext>();

    // Apply any pending EF Core migrations automatically on startup.
    // This keeps the schema consistent across builds and deployments
    // without ever dropping data. Add new migrations with:
    //   dotnet ef migrations add <MigrationName>
    db.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
