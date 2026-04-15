using System.Runtime.CompilerServices;

// Tests exercise internal emitters and schema model directly. The generator's
// public surface (attributes, settings) lives in Abstractions — internals here
// are implementation detail but well-tested.
[assembly: InternalsVisibleTo("ZibStack.NET.TypeGen.Tests")]
