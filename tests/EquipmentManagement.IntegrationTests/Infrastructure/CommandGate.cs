using System.Data;
using System.Data.Common;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Infrastructure;

/// <summary>
/// Test-only EF Core command interceptor that deterministically interleaves
/// two concurrent requests: once <see cref="Arm"/>ed, the first database
/// command matching the predicate is held just before it reaches PostgreSQL
/// until the test calls <see cref="Release"/>, so the test can run a competing
/// operation to completion in that exact gap. One-shot: it disarms itself on
/// the first match. Production code is never touched — the interceptor is
/// added only to the test host (see <see cref="CommandGateFixture"/>).
/// </summary>
public sealed class CommandGate : DbCommandInterceptor
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private Func<DbCommand, bool>? predicate;
    private TaskCompletionSource reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Arm(Func<DbCommand, bool> match)
    {
        reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref predicate, match);
    }

    /// <summary>Waits until the armed command is being held; fails if <paramref name="operation"/> finished first (the interleaving was not reproduced).</summary>
    public async Task WaitUntilHeldAsync(Task operation)
    {
        var first = await Task.WhenAny(reached.Task, operation, Task.Delay(Timeout));
        if (first != reached.Task)
        {
            Volatile.Write(ref predicate, null);
            throw new InvalidOperationException("The gated command was never reached; the interleaving was not reproduced.");
        }
    }

    public void Release() => released.TrySetResult();

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        await HoldIfMatchedAsync(command, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await HoldIfMatchedAsync(command, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        await HoldIfMatchedAsync(command, cancellationToken);
        return result;
    }

    private async Task HoldIfMatchedAsync(DbCommand command, CancellationToken cancellationToken)
    {
        var match = Volatile.Read(ref predicate);
        if (match is null || !match(command) || Interlocked.CompareExchange(ref predicate, null, match) != match)
        {
            return;
        }

        reached.TrySetResult();
        await released.Task.WaitAsync(Timeout, cancellationToken);
    }

    /// <summary>An UPDATE of the Equipment table that writes <paramref name="status"/> (EF sends the enum by name as a parameter).</summary>
    public static Func<DbCommand, bool> EquipmentStatusUpdate(string status) => command =>
        command.CommandText.Contains("UPDATE \"Equipment\"", StringComparison.Ordinal) &&
        command.Parameters.Cast<DbParameter>().Any(p => p.Value is string value && value == status);

    /// <summary>Any command whose SQL contains <paramref name="fragment"/>.</summary>
    public static Func<DbCommand, bool> Sql(string fragment) => command =>
        command.CommandText.Contains(fragment, StringComparison.Ordinal);

    /// <summary>The first command sent inside a Serializable transaction — i.e. before that transaction has taken its snapshot.</summary>
    public static Func<DbCommand, bool> FirstSerializableCommand() => command =>
        command.Transaction?.IsolationLevel == IsolationLevel.Serializable;
}

/// <summary>
/// A test host identical to <see cref="ApiWebApplicationFactory"/> (same
/// isolated database and storage) with a <see cref="CommandGate"/> added to the
/// ApplicationDbContext. <see cref="Users"/> is the plain factory, used only to
/// create test users (the helpers take that concrete type).
/// </summary>
public sealed class CommandGateFixture : IDisposable
{
    public CommandGateFixture()
    {
        Users = new ApiWebApplicationFactory();
        Factory = Users.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<ApplicationDbContext>(options => options.AddInterceptors(Gate))));
    }

    public CommandGate Gate { get; } = new();

    public ApiWebApplicationFactory Users { get; }

    public WebApplicationFactory<Program> Factory { get; }

    public async Task<HttpClient> LoginAsync(string role)
    {
        var (email, password) = await ApiTestUsers.CreateAsync(Users, role);
        var client = Factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }

    public void Dispose()
    {
        Factory.Dispose();
        Users.Dispose();
    }
}
