using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestFramework("EquipmentManagement.IntegrationTests.Infrastructure.IntegrationTestFramework", "EquipmentManagement.IntegrationTests")]

namespace EquipmentManagement.IntegrationTests.Infrastructure;

/// <summary>
/// The standard xUnit framework plus run-level teardown (ADR-074). xUnit v2 has
/// no assembly fixture, and the VSTest host neither disposes the framework nor
/// reliably runs ProcessExit handlers, so this run's <see cref="TestDatabase"/>
/// is dropped, and its <see cref="TestStorage"/> root deleted (ADR-075), from the
/// assembly runner's
/// <see cref="TestAssemblyRunner{TTestCase}.BeforeTestAssemblyFinishedAsync"/>:
/// after every collection and its fixtures have finished, still inside the run,
/// where a failure is reported as an assembly cleanup failure.
/// </summary>
public sealed class IntegrationTestFramework(IMessageSink messageSink) : XunitTestFramework(messageSink)
{
    private static IMessageSink? diagnosticSink;

    /// <summary>xUnit diagnostic message (shown with <c>xUnit.DiagnosticMessages=true</c>) — database names only, never connection strings.</summary>
    internal static void Log(string message) =>
        diagnosticSink?.OnMessage(new DiagnosticMessage($"[EquipmentManagement.IntegrationTests] {message}"));

    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName)
    {
        diagnosticSink = DiagnosticMessageSink;
        return new Executor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);
    }

    private sealed class Executor(AssemblyName assemblyName, ISourceInformationProvider sourceInformationProvider, IMessageSink diagnosticMessageSink)
        : XunitTestFrameworkExecutor(assemblyName, sourceInformationProvider, diagnosticMessageSink)
    {
        protected override async void RunTestCases(
            IEnumerable<IXunitTestCase> testCases, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
        {
            using var runner = new AssemblyRunner(TestAssembly, testCases, DiagnosticMessageSink, executionMessageSink, executionOptions);
            await runner.RunAsync();
        }
    }

    private sealed class AssemblyRunner(
        ITestAssembly testAssembly,
        IEnumerable<IXunitTestCase> testCases,
        IMessageSink diagnosticMessageSink,
        IMessageSink executionMessageSink,
        ITestFrameworkExecutionOptions executionOptions)
        : XunitTestAssemblyRunner(testAssembly, testCases, diagnosticMessageSink, executionMessageSink, executionOptions)
    {
        protected override async Task BeforeTestAssemblyFinishedAsync()
        {
            try
            {
                TestDatabase.DropIfCreated();
            }
            catch (Exception ex)
            {
                // Never retried against any other database: surfaced as a failed
                // run, and the orphan is left for a human (README "Running tests").
                Log($"FAILED to drop test database {TestDatabase.Name}: {ex.GetType().Name}");
                Aggregator.Add(ex);
            }

            try
            {
                TestStorage.DeleteIfCreated();
            }
            catch (Exception ex)
            {
                // Same rule for files (ADR-075): no broader or fallback deletion.
                Log($"FAILED to delete test storage {TestStorage.DisplayName}: {ex.GetType().Name}");
                Aggregator.Add(ex);
            }

            await base.BeforeTestAssemblyFinishedAsync();
        }
    }
}
