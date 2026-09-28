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
/// message still goes on to the runner exactly as it came.
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

        protected override void RunTestCases(IEnumerable<IXunitTestCase> testCases, IMessageSink executionMessageSink,
                                             ITestFrameworkExecutionOptions executionOptions)
        {
            var cases = testCases.ToList();
            var sink = executionMessageSink;
            try
            {
                var run = RunProgress.Begin(cases.Select(c => (c.UniqueID, ShortName(c.DisplayName))), Timings.Read());
                RunProgressWindow.OfferFor(run);
                sink = new Counting(executionMessageSink, run);
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

        public Counting(IMessageSink next, RunProgress run)
        {
            _next = next;
            _run = run;
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
                        Timings.Write(_run);
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
