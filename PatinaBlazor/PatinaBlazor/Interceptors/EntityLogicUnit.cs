using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatinaBlazor.Data;

namespace PatinaBlazor.Interceptors
{
    public enum EntityChangeType
    {
        Added,
        Modified,
        Deleted
    }

    // Non-generic so EntityLogicUnitInterceptor can hold a heterogeneous collection of
    // units closed over different T's and dispatch to each by runtime type, without
    // needing to know any concrete T at compile time itself.
    public abstract class EntityLogicUnit
    {
        private readonly ILogger _logger;

        // The DbContext instance actively mid-SaveChanges - exposed so a unit can do its
        // own explicit-load of a navigation property it needs (e.g.
        // CurrentContext.Entry(entity).Collection("Images")), the same way the original
        // ImageCleanupSaveChangesInterceptor did. This is per-entity-type knowledge the
        // generic interceptor deliberately doesn't own.
        protected DbContext CurrentContext { get; }

        // The entity's own concrete runtime type name (e.g. "Article"), captured by the
        // interceptor from entry.Entity.GetType() - not the T a unit is declared against,
        // which is often an interface like ISupportImageAttachments and wouldn't be a
        // useful filter value in the log. Attached to every LogXxx call automatically.
        protected string EntityTypeName { get; }

        // The entity's primary key value(s), captured by the interceptor. Used both to
        // stringify EntityId for logging and (in EntityLogicUnit<T>) to look up the
        // original row from a fresh context.
        protected object?[] KeyValues { get; }

        protected EntityLogicUnit(ILogger logger, DbContext currentContext, string entityTypeName, object?[] keyValues)
        {
            _logger = logger;
            CurrentContext = currentContext;
            EntityTypeName = entityTypeName;
            KeyValues = keyValues;
        }

        private string EntityIdText => KeyValues.Length == 1
            ? KeyValues[0]?.ToString() ?? ""
            : string.Join(",", KeyValues);

        protected void LogInformation(string message) => Log(LogLevel.Information, message, null);
        protected void LogWarning(string message) => Log(LogLevel.Warning, message, null);
        protected void LogError(string message) => Log(LogLevel.Error, message, null);
        protected void LogError(Exception ex, string message) => Log(LogLevel.Error, message, ex);

        // {EntityType}/{EntityId} are named message-template holes, not string
        // interpolation - Serilog captures them as structured properties under these exact
        // names, and Program.cs's MSSqlServer sink columnOptions promote matching-named
        // properties into real EntityType/EntityId columns on the Logs table.
        private void Log(LogLevel level, string message, Exception? ex) =>
            _logger.Log(level, ex, "{EntityType} {EntityId}: {Message}", EntityTypeName, EntityIdText, message);

        internal abstract Task InvokeOnSavingAsync(object entity, EntityChangeType changeType);
        internal abstract Task InvokeOnSavedAsync(object entity, EntityChangeType changeType);
    }

    // Base class for a piece of save-time logic scoped to one entity type T (a concrete
    // entity, or an interface like ISupportImageAttachments implemented by several).
    // Override OnSaving/OnSaved for whichever hook(s) you need - both default to no-ops.
    // Discovered automatically by EntityLogicUnitInterceptor via reflection; adding a new
    // implementation requires no registration anywhere.
    public abstract class EntityLogicUnit<T> : EntityLogicUnit where T : class
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        protected EntityLogicUnit(
            ILogger logger,
            DbContext currentContext,
            string entityTypeName,
            IDbContextFactory<ApplicationDbContext> contextFactory,
            object?[] keyValues)
            : base(logger, currentContext, entityTypeName, keyValues)
        {
            _contextFactory = contextFactory;
        }

        // Lazy, opt-in: a real no-tracking query against a fresh context, keyed on this
        // entity's primary key. Not eager - most units won't need it, and it costs a real
        // round trip. EF's free change-tracker OriginalValues snapshot isn't a substitute
        // here: this app's dominant delete pattern re-attaches an entity into a brand-new
        // DbContext just to remove it, which gives EF no real "before" baseline to diff
        // against (current values become the assumed original). Returns null for an Added
        // entity (no prior row exists) or if nothing matches the key.
        protected async Task<T?> GetOriginalFromDatabaseAsync()
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.FindAsync<T>(KeyValues);
        }

        public virtual Task OnSaving(T entity, EntityChangeType changeType) => Task.CompletedTask;
        public virtual Task OnSaved(T entity, EntityChangeType changeType) => Task.CompletedTask;

        internal override Task InvokeOnSavingAsync(object entity, EntityChangeType changeType) => OnSaving((T)entity, changeType);
        internal override Task InvokeOnSavedAsync(object entity, EntityChangeType changeType) => OnSaved((T)entity, changeType);
    }
}
