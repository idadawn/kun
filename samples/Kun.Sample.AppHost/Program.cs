var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .AddDatabase("kun");

var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin();

var redis = builder.AddRedis("redis");

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
