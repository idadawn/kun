var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure — containers persist across AppHost restarts (Aspire 9
// ContainerLifetime.Persistent) + named data volumes so state survives restarts
// and subsequent launches attach in ~2s instead of re-pulling/re-initializing.
var postgres = builder.AddPostgres("postgres")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("kun-postgres-data")
    .WithPgAdmin()
    .AddDatabase("kun");

var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("kun-rabbitmq-data")
    .WithManagementPlugin();

var redis = builder.AddRedis("redis")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("kun-redis-data");

// Kun services
var catalog = builder.AddProject<Projects.Kun_Sample_Catalog>("catalog")
    .WithReference(postgres)
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WaitFor(postgres)
    .WaitFor(rabbitmq);

var order = builder.AddProject<Projects.Kun_Sample_Order>("order")
    .WithReference(postgres)
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WaitFor(postgres)
    .WaitFor(rabbitmq);

var gateway = builder.AddProject<Projects.Kun_Gateway>("gateway")
    .WithReference(catalog)
    .WithReference(order);

builder.Build().Run();
