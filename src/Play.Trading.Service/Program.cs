using GreenPipes;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Play.Common.Identity;
using Play.Common.MassTransit;
using Play.Common.MongoDB;
using Play.Common.Settings;
using Play.Identity.Contracts;
using Play.Inventory.Contracts;
using Play.Trading.Service.Entities;
using Play.Trading.Service.Exceptions;
using Play.Trading.Service.Settings;
using Play.Trading.Service.SignalR;
using Play.Trading.Service.StateMachines;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers(options =>
{
    options.SuppressAsyncSuffixInActionNames = false;
}).AddJsonOptions(options => options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull);

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


builder.Services.AddMongo()
    .AddMongoRepository<CatalogItem>("catalogitems")
    .AddMongoRepository<InventoryItem>("inventoryitems")
    .AddMongoRepository<ApplicationUser>("users")
    .AddJwtBearerAuthentication();

AddMassTransit(builder.Services);


builder.Services.AddSingleton<IUserIdProvider, UserIdProvider>()
    .AddSingleton<MessageHub>()
    .AddSignalR();


var app = builder.Build();
string AllowedOriginSetting = "AllowedOrigin";

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    app.UseCors(Corsbuilder =>
    {
        Corsbuilder.WithOrigins(builder.Configuration[AllowedOriginSetting])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
}

app.UseHttpsRedirection();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.MapHub<MessageHub>("/messagehub");

app.Run();


void AddMassTransit(IServiceCollection services)
{
    services.AddMassTransit(configure =>
    {
        configure.UsingPlayEconomyRabbitMQ(retryConfigurator =>
        {
            retryConfigurator.Interval(3, TimeSpan.FromSeconds(5));
            retryConfigurator.Ignore(typeof(UnknownItemException));
        });

        configure.AddConsumers(Assembly.GetEntryAssembly());
        configure.AddSagaStateMachine<PurchaseStateMachine, PurchaseState>(sagaConfigurator =>
        {
            sagaConfigurator.UseInMemoryOutbox();
        }).MongoDbRepository(r =>
            {
                var serviceSettings = builder.Configuration.GetSection(nameof(ServiceSettings))
                                        .Get<ServiceSettings>();
                var mongoSettings = builder.Configuration.GetSection(nameof(MongoDbSettings))
                                        .Get<MongoDbSettings>();

                r.Connection = mongoSettings.ConnectionString;
                r.DatabaseName = serviceSettings.ServiceName;
            });
    });

    var queueSettings = builder.Configuration.GetSection(nameof(QueueSettings))
                        .Get<QueueSettings>();

    EndpointConvention.Map<GrantItems>(new Uri(queueSettings.GrantItemsQueueAddress));
    EndpointConvention.Map<DebitGil>(new Uri(queueSettings.DebitGilQueueAddress));
    EndpointConvention.Map<SubstractItems>(new Uri(queueSettings.SubstractItemsQueueAddress));

    services.AddMassTransitHostedService();
    services.AddGenericRequestClient();
}