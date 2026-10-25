using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: Xunit.TestFramework("SMSModForge.Tests.RunProgressFramework", "SMSModForge.Tests")]

namespace SMSModForge.Tests;

/// <summary>
/// xunit's own framework, with the run's messages read on their way past so
/// <see cref="RunProgress"/> can count them and <see cref="RunProgressWindow"/>
/// can show them. Nothing about how tests are found or run changes: every
/// message still goes on to the runner exactly as it came - except that a
/// tester of a parallel run keeps only its share of the tests (<see cref="Shards"/>).
/// </summary>
public sealed class RunProgressFramework : XunitTestFramework
{
    public RunProgressFramework(IMessageSink messageSink) : base(messageSink) { }

    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName)
        => new Executor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);

    private sealed class Executor : XunitTestFrameworkExecutor
    {
        public Executor(AssemblyName name, ISourceInformationProvider source, IMessageSink diagnostics)
            : base(name, source, diagnostics) { }

        // Kept so the collector cannot take it while the run is going.
        private static System.Threading.Timer? _publishing;

        protected override void RunTestCases(IEnumerable<IXunitTestCase> testCases, IMessageSink executionMessageSink,
                                             ITestFrameworkExecutionOptions executionOptions)
        {
            var cases = testCases.ToList();
            var last = Timings.Read();

            // Not inside the try below: a tester that could not work out its
            // share must not quietly run everybody's.
            var shard = Shards.FromEnvironment();
            (int Total, double Expected)[]? plan = null;
            if (shard is { } mine)
            {
                var all = cases.Select(c => (c.UniqueID, c.TestMethod.TestClass.Class.Name)).ToList();
                var assignment = Shards.Assign(all, last, mine.Count);
                plan = Shards.Plan(all, last, assignment, mine.Count);
                cases = cases.Where(c => assignment[c.UniqueID] == mine.Index).ToList();
            }

            var sink = executionMessageSink;
            try
            {
                var run = RunProgress.Begin(cases.Select(c => (c.UniqueID, ShortName(c.DisplayName))), last);
                if (shard is not { } sh)
                    RunProgressWindow.OfferFor(run.Now, testers: 1);
                else
                {
                    _publishing = new System.Threading.Timer(_ => Shards.Write(sh.Index, run), null, 0, 500);
                    // One window for the whole run, the first tester's.
                    if (sh.Index == 0)
                        RunProgressWindow.OfferFor(() =>
                        {
                            var others = Enumerable.Range(1, sh.Count - 1).Select(Shards.Read).ToList();
                            return Shards.Combine(run.Now(), others, others.Select(Shards.Gone).ToList(), plan!);
                        }, sh.Count);
                }
                sink = new Counting(executionMessageSink, run, shard);
            }
            catch
            {
                // Showing progress is a convenience; the run goes ahead without it.
            }
            base.RunTestCases(cases, sink, executionOptions);
        }
    }

    internal static string ShortName(string displayName)
    {
        const string ns = "SMSModForge.Tests.";
        return displayName.StartsWith(ns, StringComparison.Ordinal) ? displayName[ns.Length..] : displayName;
    }

    private sealed class Counting : Xunit.LongLivedMarshalByRefObject, IMessageSink
    {
        private readonly IMessageSink _next;
        private readonly RunProgress _run;
        private readonly (int Index, int Count)? _shard;

        public Counting(IMessageSink next, RunProgress run, (int Index, int Count)? shard)
        {
            _next = next;
            _run = run;
            _shard = shard;
        }

        public bool OnMessage(IMessageSinkMessage message)
        {
            try
            {
                switch (message)
                {
                    case ITestCaseStarting s: _run.Started(s.TestCase.UniqueID); break;
                    case ITestFailed f: _run.Failed(ShortName(f.Test.DisplayName)); break;
                    case ITestSkipped: _run.Skipped(); break;
                    case ITestCaseFinished d: _run.Finished(d.TestCase.UniqueID); break;
                    // Before passing it on: once the runner has this, the
                    // process can end at any moment.
                    case ITestAssemblyFinished:
                        _run.End();
                        if (_shard is { } sh) Shards.Finish(sh.Index, sh.Count, _run);
                        else Timings.Write(_run);
                        break;
                }
            }
            catch
            {
                // Never the reason a message did not arrive.
            }
            return _next.OnMessage(message);
        }
    }
}
