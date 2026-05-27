using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using System.Runtime;
using FraudDetection.Ivf;
using Kestrel.Transport.IoUring;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Net.Http.Headers;

namespace FraudDetection;

public class Program
{
   public static void Main(string[] args)
   {
      GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

      ThreadPool.SetMinThreads(workerThreads: 4, completionPortThreads: 1);
      ThreadPool.SetMaxThreads(workerThreads: 4, completionPortThreads: 2);
      
      var builder = WebApplication.CreateSlimBuilder(args);

      builder.Logging.ClearProviders();

      // Wire up source-generated serializer
      builder.Services.ConfigureHttpJsonOptions(opts =>
         opts.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));

      if (false)
      {
         Console.WriteLine("Using IO ring");
         builder.WebHost.UseIoUring(opts =>
         {
            //var ringSize = int.TryParse(Environment.GetEnvironmentVariable("IO_URING_SIZE"), out var rs) && rs > 0 ? rs : 256;
            //var maxConn = int.TryParse(Environment.GetEnvironmentVariable("IO_URING_MAX_CONN"), out var mc) && mc > 0 ? mc : 1024;
            opts.RingSize = 4096;
            opts.MaxConnections = 1024;
         });
      }

      var socketPath = Environment.GetEnvironmentVariable("SOCKET_PATH");
      if (!string.IsNullOrEmpty(socketPath))
      {
         if (File.Exists(socketPath))
         {
            File.Delete(socketPath);
         }
      }

      builder.WebHost.ConfigureKestrel(options =>
      {
         if (!string.IsNullOrEmpty(socketPath))
         {
            options.ListenUnixSocket(socketPath, o => o.Protocols = HttpProtocols.Http1);
         }
         else
         {
            options.ListenAnyIP(9999, o => o.Protocols = HttpProtocols.Http1);
         }
         options.AddServerHeader = false;
         options.AllowSynchronousIO = false;
         options.Limits.MaxRequestBodySize = 8 * 1024;
         options.Limits.MaxRequestHeadersTotalSize = 4 * 1024;
         options.Limits.MaxRequestLineSize = 1 * 1024;
         options.Limits.MaxConcurrentUpgradedConnections = 0;
         options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(5);
         options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
      });

      Console.WriteLine($"SIMD Vector support is {Vector<float>.Count} floats wide");

      Console.WriteLine("Loading references");
      ReferenceStore refStore = new(builder.Configuration);
      
      var index = new IvfIndex();
      Console.WriteLine("Initializing service");
      var stopwatch = Stopwatch.StartNew();
      index.Build(refStore.References, kmeansSeed: 42); // seed makes builds reproducible
      stopwatch.Stop();
      Console.WriteLine($"Index built in {stopwatch.Elapsed.TotalSeconds:F1}s");

      //var index = new BruteForceFinder(refStore);
      var vectorizer = new TransactionVectorizer(builder.Configuration);   // Force initialization of service now

      GC.Collect();
      Console.WriteLine("Done initializing service");
      

      // Add services to the container.
      var app = builder.Build();

      app.MapGet("/ready", () => TypedResults.Ok());

      app.MapPost("/fraud-score", async (FraudScoreRequest req, HttpContext ctx) =>
      {
         Span<float> transVector = stackalloc float[16];
         vectorizer.Vectorize(req, transVector);
         var result = index.Search(transVector);
         var response = PrecomputedResponses.GetResponse(result);

         ctx.Response.StatusCode = 200;
         ctx.Response.ContentType = "application/json";
         ctx.Response.ContentLength = response.Length;
         ctx.Response.Headers.Remove(HeaderNames.Date);   // Suppress Kestrel's per-request Date header (~25B + cost of formatting).
         var writer = ctx.Response.BodyWriter;
         writer.Write(response.AsSpan());
         var ft = writer.FlushAsync();
         if (!ft.IsCompletedSuccessfully)
         {
            await ft.ConfigureAwait(false);
         }
      });

      app.Run();
   }
}
