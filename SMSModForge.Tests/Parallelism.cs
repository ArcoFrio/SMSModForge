using Xunit;

// Run the test classes one at a time.
//
// Every test here builds a MainViewModel, and constructing one assigns a
// handful of STATIC hooks that the view models read back through —
// NodeActionViewModel.OverlayLevelProvider and .IsBoolVariableLookup,
// NodeConditionViewModel.IsBoolVariableLookup, InferredOverlayLevel. They are
// static because a row deep in a template has no path to the window's view
// model, which is fine in an application that has exactly one.
//
// A test run does not. With classes running in parallel, two live view models
// share those hooks: the last one constructed wins, and a provider invoked on
// behalf of one walker then reads the OTHER walker's collections while it is
// still mutating them. That surfaced once as "Collection was modified;
// enumeration operation may not execute" inside an ObservableCollection.Add,
// in a test that had nothing to do with overlays.
//
// The whole suite runs in about a second, so serialising it costs nothing
// worth measuring, and an intermittent failure in a harness whose job is to
// catch regressions is worse than useless — it teaches people to re-run it.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
