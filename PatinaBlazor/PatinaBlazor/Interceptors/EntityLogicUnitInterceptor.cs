using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PatinaBlazor.Data;

namespace PatinaBlazor.Interceptors
{
    // Generic extension point for save-time entity logic: discovers every concrete
    // EntityLogicUnit<T> subclass in the assembly via reflection and dispatches
    // OnSaving/OnSaved to whichever ones apply to each entity being saved. This
    // interceptor has no entity-specific knowledge of its own - adding a new
    // EntityLogicUnit<T> (e.g. ImageCleanupLogicUnit) requires no change here and no
    // registration in Program.cs.
    //
    // "What applies, and what each unit captured in OnSaving" is stashed in
    // SavingChangesAsync, before the row (and any cascade-deleted rows) are actually
    // removed - OnSaved runs after the save has committed. State (including the
    // IServiceScope units were constructed from, kept alive until OnSaved is done with it)
    // is keyed per DbContext instance via ConditionalWeakTable rather than an instance
    // field, since this interceptor is registered once (singleton) and shared across every
    // short-lived DbContext the app's IDbContextFactory creates - an instance field would
    // let concurrent SaveChanges calls from different contexts corrupt each other's state.
    //
    // Each unit logs through its own ILogger (category = the unit's own concrete type
    // name), resolved per-invocation via ILoggerFactory rather than a shared error list -
    // Serilog (see Program.cs) is the actual persistence for anything a unit logs, so this
    // interceptor no longer aggregates or rolls up log output itself.
    public class EntityLogicUnitInterceptor : SaveChangesInterceptor
    {
        private readonly IServiceScopeFactory _scopeFactory;

        private static readonly Lazy<List<Type>> DiscoveredUnitTypes = new(() =>
            typeof(EntityLogicUnit).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && FindEntityLogicUnitGenericBase(t) != null)
                .ToList());

        private static Type? FindEntityLogicUnitGenericBase(Type type)
        {
            for (var baseType = type.BaseType; baseType != null; baseType = baseType.BaseType)
            {
                if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(EntityLogicUnit<>))
                {
                    return baseType;
                }
            }

            return null;
        }

        private static Type GetEntityType(Type unitType) =>
            FindEntityLogicUnitGenericBase(unitType)!.GetGenericArguments()[0];

        private sealed record PendingUnit(EntityLogicUnit Unit, object Entity, EntityChangeType ChangeType);

        private sealed class PendingSaveState : IDisposable
        {
            public IServiceScope Scope { get; }
            public List<PendingUnit> Units { get; } = new();

            public PendingSaveState(IServiceScope scope) => Scope = scope;

            public void Dispose() => Scope.Dispose();
        }

        private static readonly ConditionalWeakTable<DbContext, PendingSaveState> PendingSaves = new();

        public EntityLogicUnitInterceptor(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } context && DiscoveredUnitTypes.Value.Count > 0)
            {
                var scope = _scopeFactory.CreateScope();
                var state = new PendingSaveState(scope);

                try
                {
                    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

                    foreach (var entry in context.ChangeTracker.Entries().ToList())
                    {
                        var changeType = ToChangeType(entry.State);
                        if (changeType is null)
                        {
                            continue;
                        }

                        foreach (var unitType in DiscoveredUnitTypes.Value)
                        {
                            var entityType = GetEntityType(unitType);
                            if (!entityType.IsInstanceOfType(entry.Entity))
                            {
                                continue;
                            }

                            var keyValues = entry.Metadata.FindPrimaryKey()!.Properties
                                .Select(p => entry.Property(p.Name).CurrentValue)
                                .ToArray();

                            var logger = loggerFactory.CreateLogger(unitType.FullName ?? unitType.Name);

                            var unit = (EntityLogicUnit)ActivatorUtilities.CreateInstance(
                                scope.ServiceProvider, unitType, logger, context, entry.Entity.GetType().Name, keyValues);

                            await unit.InvokeOnSavingAsync(entry.Entity, changeType.Value);
                            state.Units.Add(new PendingUnit(unit, entry.Entity, changeType.Value));
                        }
                    }

                    if (state.Units.Count > 0)
                    {
                        PendingSaves.AddOrUpdate(context, state);
                    }
                    else
                    {
                        state.Dispose();
                    }
                }
                catch
                {
                    state.Dispose();
                    throw;
                }
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } context && PendingSaves.TryGetValue(context, out var state))
            {
                PendingSaves.Remove(context);

                try
                {
                    foreach (var pending in state.Units)
                    {
                        await pending.Unit.InvokeOnSavedAsync(pending.Entity, pending.ChangeType);
                    }
                }
                finally
                {
                    state.Dispose();
                }
            }

            return await base.SavedChangesAsync(eventData, result, cancellationToken);
        }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } context && PendingSaves.TryGetValue(context, out var state))
            {
                PendingSaves.Remove(context);
                state.Dispose();
            }

            return base.SaveChangesFailedAsync(eventData, cancellationToken);
        }

        private static EntityChangeType? ToChangeType(EntityState state) => state switch
        {
            EntityState.Added => EntityChangeType.Added,
            EntityState.Modified => EntityChangeType.Modified,
            EntityState.Deleted => EntityChangeType.Deleted,
            _ => null
        };
    }
}
