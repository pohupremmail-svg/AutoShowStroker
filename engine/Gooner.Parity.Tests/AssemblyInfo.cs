// AppLogParity switches the one process-wide log on and compares the file line by line; another
// case logging a warning at the same time would land in it. The run is short, so it runs in
// sequence.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
