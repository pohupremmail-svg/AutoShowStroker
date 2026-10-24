// The log is one sink for the whole process: a test class logging a warning while AppLogTests has
// file logging switched on would land in that test's file and break what it asserts about it. The
// suite runs in well under a second, so running it in sequence costs nothing.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
