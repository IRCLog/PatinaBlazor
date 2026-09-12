using Microsoft.EntityFrameworkCore;
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
        private readonly List<string> _errors;

        // The DbContext instance actively mid-SaveChanges - exposed so a unit can do its
        // own explicit-load of a navigation property it needs (e.g.
        // CurrentContext.Entry(entity).Collection("Images")), the same way the original
        // ImageCleanupSaveChangesInterceptor did. This is per-entity-type knowledge the
        // generic interceptor deliberately doesn't own.
        protected DbContext CurrentContext { get; }

        protected EntityLogicUnit(List<string> errors, DbContext currentContext)
        {
            _errors = errors;
            CurrentContext = currentContext;
        }

        protected void LogError(string message) => _errors.Add(message);
        protected void LogErrors(IEnumerable<string> messages) => _errors.AddRange(messages);

        // The entity type (often an interface, e.g. ISupportImageAttachments) this unit
        // applies to - used by the interceptor to test whether a given tracked entity
        // should be dispatched to this unit.
        public abstract Type EntityType { get; }

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
        private readonly object?[] _keyValues;

        protected EntityLogicUnit(
            List<string> errors,
            DbContext currentContext,
            IDbContextFactory<ApplicationDbContext> contextFactory,
            object?[] keyValues)
            : base(errors, currentContext)
        {
            _contextFactory = contextFactory;
            _keyValues = keyValues;
        }

        public override Type EntityType => typeof(T);

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
            return await context.FindAsync<T>(_keyValues);
        }

        public virtual Task OnSaving(T entity, EntityChangeType changeType) => Task.CompletedTask;
        public virtual Task OnSaved(T entity, EntityChangeType changeType) => Task.CompletedTask;

        internal override Task InvokeOnSavingAsync(object entity, EntityChangeType changeType) => OnSaving((T)entity, changeType);
        internal override Task InvokeOnSavedAsync(object entity, EntityChangeType changeType) => OnSaved((T)entity, changeType);
    }
}
