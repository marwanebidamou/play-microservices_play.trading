using Automatonymous;
using MassTransit;
using Play.Identity.Contracts;
using Play.Inventory.Contracts;
using Play.Trading.Service.Activiies;
using Play.Trading.Service.SignalR;

namespace Play.Trading.Service.StateMachines
{
    public class PurchaseStateMachine : MassTransitStateMachine<PurchaseState>
    {
        private readonly MessageHub _hub;

        public State Accepted { get; set; }
        public State ItemsGranted { get; set; }
        public State Completed { get; set; }
        public State Faulted { get; set; }

        public Event<PurchaseRequested> PurchaseRequested { get; }
        public Event<GetPurchaseState> GetPurchaseState { get; }
        public Event<InventoryItemsGranted> InventoryItemsGranted { get; }
        public Event<GilDebited> GilDebited { get; }

        //Compensating events
        public Event<Fault<GrantItems>> GrantitemsFaulted { get; }
        public Event<Fault<DebitGil>> DebitGilFaulted { get; }

        public PurchaseStateMachine(MessageHub hub)
        {
            InstanceState(state => state.CurrentState);
            ConfigureEvents();
            ConfigureInitialState();
            ConfigureAny();
            ConfigureAccepted();
            ConfigureItemsGranted();
            ConfigureFaulted();
            ConfigureCompleted();
            _hub = hub;
        }

        private void ConfigureEvents()
        {
            Event(() => PurchaseRequested);
            Event(() => GetPurchaseState);
            Event(() => InventoryItemsGranted);
            Event(() => GilDebited);

            //Compensating events
            //nb: we gonna specify manually what is the correlation id,
            //since masstransit can't know it automatically cause it's faulted event
            Event(() => GrantitemsFaulted,x=>x.CorrelateById(
                context => context.Message.Message.CorrelationId));
            Event(() => DebitGilFaulted, x => x.CorrelateById(
                context => context.Message.Message.CorrelationId));

        }

        private void ConfigureInitialState()
        {
            Initially(
                When(PurchaseRequested)
                    .Then(context =>
                    {
                        context.Instance.UserId = context.Data.UserId;
                        context.Instance.ItemId = context.Data.ItemId;
                        context.Instance.Quantity = context.Data.Quantity;
                        context.Instance.Received = DateTimeOffset.UtcNow;
                        context.Instance.LastUpdated = context.Instance.Received;
                    })
                    .Activity(x => x.OfType<CalculatePurchaseTotalActivity>())
                    .Send(context => new GrantItems
                    (
                        UserId: context.Instance.UserId,
                        CatalogItemId: context.Instance.ItemId,
                        Quantity: context.Instance.Quantity,
                        CorrelationId: context.Instance.CorrelationId
                    ))
                    .TransitionTo(Accepted)
                    .Catch<Exception>(ex => ex
                        .Then(context =>
                        {
                            context.Instance.ErrorMessage = context.Exception.Message;
                            context.Instance.LastUpdated = DateTimeOffset.UtcNow;
                        })
                        .TransitionTo(Faulted)
                        .ThenAsync(async context =>await _hub.SendStatusAsync(context.Instance)))
            );
        }

        private void ConfigureAccepted()
        {
            During(Accepted,
                Ignore(PurchaseRequested),
                When(InventoryItemsGranted)
                    .Then(context =>
                    {
                        context.Instance.LastUpdated = DateTimeOffset.UtcNow;

                    })
                    .Send(context => new DebitGil(
                        UserId: context.Instance.UserId,
                        Gil: context.Instance.PuschaseTotal.Value,
                        CorrelationId: context.Instance.CorrelationId))
                    .TransitionTo(ItemsGranted),
                When(GrantitemsFaulted)
                    .Then(context =>
                    {
                        context.Instance.ErrorMessage = context.Data.Exceptions.FirstOrDefault()?.Message;
                        context.Instance.LastUpdated = DateTimeOffset.UtcNow;
                    })
                    .TransitionTo(Faulted)
                    .ThenAsync(async context => await _hub.SendStatusAsync(context.Instance))
                );
        }

        private void ConfigureItemsGranted()
        {
            During(ItemsGranted,
                Ignore(PurchaseRequested),
                Ignore(InventoryItemsGranted),
                When(GilDebited)
                    .Then(context =>
                    {
                        context.Instance.LastUpdated = DateTimeOffset.UtcNow;
                    })
                    .TransitionTo(Completed)
                    .ThenAsync(async context => await _hub.SendStatusAsync(context.Instance)),
                When(DebitGilFaulted)
                    .Send(context => new SubstractItems
                        (
                            UserId:context.Instance.UserId,
                            CatalogItemId: context.Instance.ItemId,
                            Quantity: context.Instance.Quantity,
                            CorrelationId: context.Instance.CorrelationId
                        ))
                    .Then(context =>
                    {
                        context.Instance.ErrorMessage = context.Data.Exceptions.FirstOrDefault()?.Message;
                        context.Instance.LastUpdated = DateTimeOffset.UtcNow;
                    })
                    .TransitionTo(Faulted)
                    .ThenAsync(async context => await _hub.SendStatusAsync(context.Instance))
                );
        }

        private void ConfigureCompleted()
        {
            During(Completed,
                Ignore(PurchaseRequested),
                Ignore(InventoryItemsGranted),
                Ignore(GilDebited)
                );
        }

        private void ConfigureAny()
        {
            DuringAny(
                When(GetPurchaseState)
                    .Respond(x => x.Instance)
            );
        }

        /// <summary>
        /// if any of other messages or events try to arrive to the state machine
        /// while we're in faulted state
        /// </summary>
        private void ConfigureFaulted()
        {
            During(Faulted,
                Ignore(PurchaseRequested),
                Ignore(InventoryItemsGranted),
                Ignore(GilDebited)
                );
        }

    }
}
