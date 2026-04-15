// Polyfill so `record` and init-only setters compile on netstandard2.0. The
// generator targets netstandard2.0 because that's what Roslyn analyzers require —
// modern record syntax wouldn't otherwise be available without this shim.
namespace System.Runtime.CompilerServices
{
    using System.ComponentModel;

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit { }
}
