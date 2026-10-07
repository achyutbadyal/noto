using Microsoft.Data.Sqlite;
using Noto.Core.Interfaces;
using Noto.Core.Sync;
using Noto.Data.Repositories;

namespace Noto.Data;

// One connection, one writer at a time: all access is serialized through RunAsync.
public sealed partial class SqliteUnitOfWork : IUnitOfWork, IDisposable
{
    readonly SqliteConnection _conn;
    readonly SemaphoreSlim _gate = new(1, 1);

    readonly OpRecorder? _recorder;

    // Pass the device's HybridClock to record synced changes as ops; without it nothing is recorded (local-only use).
    public SqliteUnitOfWork(string connectionString, HybridClock? hlc = null)
    {
        _recorder = hlc is null ? null : new OpRecorder(hlc);
        _conn = new SqliteConnection(connectionString);
        _conn.Open();
        using (var pragma = _conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;";
            pragma.ExecuteNonQuery();
        }
        Migrator.Apply(_conn);
    }

    public static SqliteUnitOfWork InMemory(HybridClock? hlc = null) =>
        new("Data Source=:memory:", hlc);

    public async Task<T> RunAsync<T>(Func<IStore, Task<T>> work)
    {
        await _gate.WaitAsync();
        try
        {
            await using var tx = (SqliteTransaction)await _conn.BeginTransactionAsync();
            var result = await work(new Store(_conn, tx, _recorder));
            await tx.CommitAsync();
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _conn.Dispose();
        _gate.Dispose();
    }

    sealed class Store : IStore
    {
        public Store(SqliteConnection conn, SqliteTransaction tx, OpRecorder? recorder)
        {
            var sync = new SyncStore(conn, tx);
            var rec = recorder is null ? null : new ChangeRecorder(recorder, conn, tx, sync);
            if (rec is not null)
                sync.Attach(rec);

            Items = new ItemRepository(conn, tx, rec);
            Workspaces = new WorkspaceRepository(conn, tx, rec);
            Events = new EventStore(conn, tx, rec);
            Caches = new CacheStore(conn, tx);
            Sync = sync;
            SyncRows = rec?.Rows ?? new SyncRowStore(conn, tx);
            Rules = new RuleRepository(conn, tx, rec);
            Tags = new TagRepository(conn, tx, rec);
            DayNotes = new DayNoteRepository(conn, tx, rec);
            Links = new LinkRepository(conn, tx, rec);
            Previews = new PreviewCacheRepository(conn, tx);
            Connections = new ConnectionRepository(conn, tx);
        }

        public IItemRepository Items { get; }
        public IWorkspaceRepository Workspaces { get; }
        public IEventStore Events { get; }
        public ICacheStore Caches { get; }
        public ISyncStore Sync { get; }
        public ISyncRowStore SyncRows { get; }
        public IRecurrenceRuleRepository Rules { get; }
        public ITagRepository Tags { get; }
        public IDayNoteRepository DayNotes { get; }
        public ILinkRepository Links { get; }
        public IPreviewCache Previews { get; }
        public IConnectionRepository Connections { get; }
    }
}
