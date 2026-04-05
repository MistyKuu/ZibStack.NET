// LogAspectEmitter — placeholder for v2 migration to AopPipeline.
// Currently, ZibLogGenerator uses its own pipeline (ZibLogParser + ZibLogEmitter).
// In v2, this emitter will implement IAspectEmitter and plug into AopPipeline,
// allowing [Log] to coexist with other aspects on the same method.
