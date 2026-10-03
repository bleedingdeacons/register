// Scenarios run one at a time.
//
// Two things in Register.Core are process-wide. AppLogger hands out loggers
// from Inventory's CurrentLogger, a static; and TemporaryIdGenerator keeps
// its counter in a static field, so a scenario that creates a member would
// see another scenario's ids. Left parallel, a scenario about temporary ids
// could fail for reasons that have nothing to do with the code.
//
// The alternative is turning those statics into instances threaded through
// the app, which is a production change made to suit a test host. This
// suite is a few dozen in-memory scenarios; serialising it costs seconds.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
