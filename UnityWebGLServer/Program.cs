using System.IO.Compression;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

// Output Cache Settings
builder.Services.AddOutputCache(options =>
{
    options.AddBasePolicy(builder =>
    {
        builder.Expire(TimeSpan.FromSeconds(120));
        builder.Cache();
    });
});


// Response Compression Settings
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
    new[] { "image/png" });
});

builder.Services.AddRequestDecompression();


// File Compression Settings
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.SmallestSize;
});

builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.SmallestSize;
});


// Rate Limiter Settings
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Request.Headers.Host.ToString(),
        factory: partition => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromSeconds(60)
        }
     ));
});


// Set up custom content types - associating file extension to MIME type
var provider = new FileExtensionContentTypeProvider();
provider.Mappings[".data"] = "application/octet-stream";
provider.Mappings[".wasm"] = "application/wasm";


// File Cache Settings
var GlobalCacheSettings = new CacheControlHeaderValue
{
    NoCache = true,
    NoStore = true,
    MustRevalidate = true,
};


var app = builder.Build();


app.UseRateLimiter();
app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseRequestDecompression();
app.UseOutputCache();

// Special Set-up For Build Folder
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(Directory.GetCurrentDirectory(), "UnityFiles", "Build")),
    ServeUnknownFileTypes = true,
    RequestPath = "/Build",
    HttpsCompression = HttpsCompressionMode.Compress,
    ContentTypeProvider = provider,

    OnPrepareResponse = (context) =>
    {
        var headers = context.Context.Response.GetTypedHeaders();

        headers.CacheControl = GlobalCacheSettings;

        if (context.File.Name.EndsWith(".data.br"))
        {
            headers.Append("Content-Type", "application/octet-stream");
            headers.Append("Content-Encoding", "br");
        }
        else if (context.File.Name.EndsWith(".wasm.br"))
        {
            headers.Append("Content-Type", "application/wasm");
            headers.Append("Content-Encoding", "br");
        }
        else if (context.File.Name.EndsWith(".js.br"))
        {
            headers.Append("Content-Type", "application/javascript");
            headers.Append("Content-Encoding", "br");
        }
    }
});


// Global File Server Settings
var globalFileServerOptions = new FileServerOptions()
{
    FileProvider = new PhysicalFileProvider(Path.Combine(Directory.GetCurrentDirectory(), "UnityFiles")),
    RequestPath = String.Empty,
    EnableDefaultFiles = true,
};

globalFileServerOptions.StaticFileOptions.ServeUnknownFileTypes = true;
globalFileServerOptions.StaticFileOptions.HttpsCompression = HttpsCompressionMode.Compress;
globalFileServerOptions.StaticFileOptions.OnPrepareResponse = (context) =>
{
    context.Context.Response.GetTypedHeaders().CacheControl = GlobalCacheSettings;
};

app.UseFileServer(globalFileServerOptions);

app.Run();
