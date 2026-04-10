using CommunityToolkit.Aspire.Hosting.Garage.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient<GarageApiClient>(client =>
    client.BaseAddress = new Uri("https+http://apiservice"));

var app = builder.Build();

app.MapDefaultEndpoints();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

// Proxy download endpoint — lets the browser download objects through the Blazor server.
app.MapGet("/download/{bucket}/{**key}", async (string bucket, string key, GarageApiClient api) =>
{
    var stream = await api.DownloadObjectAsync(bucket, key);
    var fileName = Path.GetFileName(key);
    return Results.File(stream, "application/octet-stream", fileName);
});

app.MapRazorComponents<CommunityToolkit.Aspire.Hosting.Garage.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
