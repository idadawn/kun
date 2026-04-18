// Placeholder entry point. The real YARP gateway host (S2S JWT issuance,
// rate limiting, gRPC-Web transcoding) is implemented in Phase 5.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/", () => "Kun.Gateway placeholder — implementation lands in Phase 5.");
app.Run();
